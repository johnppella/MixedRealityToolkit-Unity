﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.MixedReality.Toolkit.Internal.EventDatum.Teleport;
using Microsoft.MixedReality.Toolkit.Internal.Interfaces.InputSystem;
using Microsoft.MixedReality.Toolkit.Internal.Interfaces.TeleportSystem;
using Microsoft.MixedReality.Toolkit.Internal.Managers;
using Microsoft.MixedReality.Toolkit.Internal.Utilities;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Microsoft.MixedReality.Toolkit.SDK.Teleportation
{
    /// <summary>
    /// The Mixed Reality Toolkit's specific implementation of the <see cref="IMixedRealityTeleportSystem"/>
    /// </summary>
    public class MixedRealityTeleportManager : MixedRealityEventManager, IMixedRealityTeleportSystem
    {
        private TeleportEventData teleportEventData;

        private bool isTeleporting = false;
        private bool isProcessingTeleportRequest = false;

        private Vector3 startRotation = Vector3.zero;

        private Vector3 targetPosition = Vector3.zero;
        private Vector3 targetRotation = Vector3.zero;

        #region IMixedRealityManager Implementation

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();
            InitializeInternal();
        }

        private void InitializeInternal()
        {
            if (CameraCache.Main.transform.parent == null)
            {
                var cameraParent = new GameObject("Body");
                CameraCache.Main.transform.SetParent(cameraParent.transform);
            }

            // Make sure the camera is at the scene origin.
            CameraCache.Main.transform.parent.transform.position = Vector3.zero;
            CameraCache.Main.transform.localPosition = Vector3.zero;

#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying)
            {
                var eventSystems = Object.FindObjectsOfType<EventSystem>();

                if (eventSystems.Length == 0)
                {
                    if (!MixedRealityManager.Instance.ActiveProfile.IsInputSystemEnabled)
                    {
                        new GameObject("Event System").AddComponent<EventSystem>();
                    }
                    else
                    {
                        Debug.Log("The Input System didn't properly add an event system to your scene. Please make sure the Input System's priority is set higher than the teleport system.");
                    }
                }
                else if (eventSystems.Length > 1)
                {
                    Debug.Log("Too many event systems in the scene. The Teleport System requires only one.");
                }
            }
#endif // UNITY_EDITOR

            TeleportDuration = MixedRealityManager.Instance.ActiveProfile.TeleportDuration;
            teleportEventData = new TeleportEventData(EventSystem.current);
        }

        #endregion IMixedRealityManager Implementation

        #region IEventSystemManager Implementation

        /// <inheritdoc />
        public override void HandleEvent<T>(BaseEventData eventData, ExecuteEvents.EventFunction<T> eventHandler)
        {
            Debug.Assert(eventData != null);
            var teleportData = ExecuteEvents.ValidateEventData<TeleportEventData>(eventData);
            Debug.Assert(teleportData != null);
            Debug.Assert(!teleportData.used);

            // Process all the event listeners
            base.HandleEvent(teleportData, eventHandler);
        }

        /// <summary>
        /// Unregister a <see cref="GameObject"/> from listening to Teleport events.
        /// </summary>
        /// <param name="listener"></param>
        public override void Register(GameObject listener)
        {
            base.Register(listener);
        }

        /// <summary>
        /// Unregister a <see cref="GameObject"/> from listening to Teleport events.
        /// </summary>
        /// <param name="listener"></param>
        public override void Unregister(GameObject listener)
        {
            base.Unregister(listener);
        }

        #endregion IEventSystemManager Implementation

        #region IMixedRealityTeleportSystem Implementation

        private float teleportDuration = 0.25f;

        /// <inheritdoc />
        public float TeleportDuration
        {
            get { return teleportDuration; }
            set
            {
                if (isProcessingTeleportRequest)
                {
                    Debug.LogWarning("Couldn't change teleport duration. Teleport in progress.");
                    return;
                }

                teleportDuration = value;
            }
        }

        private static readonly ExecuteEvents.EventFunction<IMixedRealityTeleportHandler> OnTeleportRequestHandler =
            delegate (IMixedRealityTeleportHandler handler, BaseEventData eventData)
            {
                var casted = ExecuteEvents.ValidateEventData<TeleportEventData>(eventData);
                handler.OnTeleportRequest(casted);
            };

        /// <inheritdoc />
        public void RaiseTeleportRequest(IMixedRealityPointer pointer, IMixedRealityTeleportHotSpot hotSpot)
        {
            // initialize event
            teleportEventData.Initialize(pointer, hotSpot);

            // Pass handler
            HandleEvent(teleportEventData, OnTeleportRequestHandler);
        }

        private static readonly ExecuteEvents.EventFunction<IMixedRealityTeleportHandler> OnTeleportStartedHandler =
            delegate (IMixedRealityTeleportHandler handler, BaseEventData eventData)
            {
                var casted = ExecuteEvents.ValidateEventData<TeleportEventData>(eventData);
                handler.OnTeleportStarted(casted);
            };

        /// <inheritdoc />
        public void RaiseTeleportStarted(IMixedRealityPointer pointer, IMixedRealityTeleportHotSpot hotSpot)
        {
            if (isTeleporting)
            {
                Debug.LogError("Teleportation already in progress");
                return;
            }

            isTeleporting = true;

            // initialize event
            teleportEventData.Initialize(pointer, hotSpot);

            // Pass handler
            HandleEvent(teleportEventData, OnTeleportStartedHandler);

            ProcessTeleportationRequest(teleportEventData);
        }

        private static readonly ExecuteEvents.EventFunction<IMixedRealityTeleportHandler> OnTeleportCompletedHandler =
            delegate (IMixedRealityTeleportHandler handler, BaseEventData eventData)
            {
                var casted = ExecuteEvents.ValidateEventData<TeleportEventData>(eventData);
                handler.OnTeleportCompleted(casted);
            };

        /// <inheritdoc />
        public void RaiseTeleportComplete(IMixedRealityPointer pointer, IMixedRealityTeleportHotSpot hotSpot)
        {
            // Check to make sure no one from outside the Teleport System called this method.
            // Other implementations may have a different way of processing requests.
            if (isProcessingTeleportRequest)
            {
                Debug.LogError("Calls to this method from outside the Teleport System is not allowed in this implementation.");
                return;
            }

            if (!isTeleporting)
            {
                Debug.LogError("No Active Teleportation in progress.");
                return;
            }

            // initialize event
            teleportEventData.Initialize(pointer, hotSpot);

            // Pass handler
            HandleEvent(teleportEventData, OnTeleportCompletedHandler);

            isTeleporting = false;
        }

        private static readonly ExecuteEvents.EventFunction<IMixedRealityTeleportHandler> OnTeleportCanceledHandler =
            delegate (IMixedRealityTeleportHandler handler, BaseEventData eventData)
            {
                var casted = ExecuteEvents.ValidateEventData<TeleportEventData>(eventData);
                handler.OnTeleportCanceled(casted);
            };

        /// <inheritdoc />
        public void RaiseTeleportCanceled(IMixedRealityPointer pointer, IMixedRealityTeleportHotSpot hotSpot)
        {
            // initialize event
            teleportEventData.Initialize(pointer, hotSpot);

            // Pass handler
            HandleEvent(teleportEventData, OnTeleportCanceledHandler);
        }

        #endregion IMixedRealityTeleportSystem Implementation

        private void ProcessTeleportationRequest(TeleportEventData eventData)
        {
            isProcessingTeleportRequest = true;

            var cameraParent = CameraCache.Main.transform.parent;

            startRotation = CameraCache.Main.transform.eulerAngles;
            startRotation.x = 0f;
            startRotation.z = 0f;

            cameraParent.eulerAngles = startRotation;

            if (eventData.HotSpot != null)
            {
                targetPosition = eventData.HotSpot.Position;
                targetRotation.y = eventData.HotSpot.OverrideTargetOrientation
                    ? eventData.HotSpot.TargetOrientation
                    : eventData.Pointer.PointerOrientation;
            }
            else
            {
                targetPosition = eventData.Pointer.Result.Details.Point;
                targetRotation.y = eventData.Pointer.PointerOrientation;
            }

            float yAxis = targetPosition.y;
            targetPosition -= CameraCache.Main.transform.position - CameraCache.Main.transform.parent.position;
            targetPosition.y = yAxis;
            cameraParent.position = targetPosition;
            cameraParent.eulerAngles = targetRotation;

            isProcessingTeleportRequest = false;

            // Raise complete event using the pointer and hot spot provided.
            RaiseTeleportComplete(eventData.Pointer, eventData.HotSpot);
        }
    }
}
