// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.MixedReality.Toolkit.Core.Definitions.Utilities;
using Microsoft.MixedReality.Toolkit.Core.EventDatum.Input;
using Microsoft.MixedReality.Toolkit.Core.Interfaces.InputSystem;
using Microsoft.MixedReality.Toolkit.Core.Interfaces.InputSystem.Handlers;
using Microsoft.MixedReality.Toolkit.Core.Utilities.Physics;
using Microsoft.MixedReality.Toolkit.Core.Services;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;

namespace Microsoft.MixedReality.Toolkit.SDK.UX
{
    /// <summary>
    /// This script allows for an object to be movable, scalable, and rotatable with one or two hands. 
    /// You may also configure the script on only enable certain manipulations. The script works with 
    /// both HoloLens' gesture input and immersive headset's motion controller input.
    /// See Assets/HoloToolkit-Examples/Input/Readme/README_TwoHandManipulationTest.md
    /// for instructions on how to use the script.
    /// </summary>
    public class TwoHandManipulatable : MonoBehaviour, IMixedRealitySourceStateHandler, IMixedRealityInputHandler
    {
        [SerializeField]
        [Tooltip("Transform that will be dragged. Defaults to the object of the component.")]
        private Transform hostTransform = null;

        public Transform HostTransform
        {
            get { return hostTransform; }
            set { hostTransform = value; }
        }

        [SerializeField]
        [Tooltip("What manipulation will two hands perform?")]
        private TwoHandedManipulationType manipulationMode = TwoHandedManipulationType.Scale;

        public TwoHandedManipulationType ManipulationMode
        {
            get { return manipulationMode; }
            set { manipulationMode = value; }
        }

        [SerializeField]
        [Tooltip("Constrain rotation along an axis")]
        private RotationConstraintType rotationConstraint = RotationConstraintType.None;

        public RotationConstraintType RotationConstraint
        {
            get { return rotationConstraint; }
            set { rotationConstraint = value; }
        }

        [SerializeField]
        [Tooltip("If true, grabbing the object with one hand will initiate movement.")]
        private bool enableOneHandMovement = true;


        private static IMixedRealityInputSystem inputSystem = null;

        /// <summary>
        /// The current Input System registered with the Mixed Reality Toolkit.
        /// </summary>
        public static IMixedRealityInputSystem InputSystem => inputSystem ?? (inputSystem = MixedRealityToolkit.Instance.GetService<IMixedRealityInputSystem>());

        public bool EnableEnableOneHandedMovement
        {
            get { return enableOneHandMovement; }
            set { enableOneHandMovement = value; }
        }

        // Private fields that store transform information.
        #region Transform Info

        //private BoundingBox boundingBoxInstance;
        private TwoHandedManipulationType currentState;
        private TwoHandMoveLogic moveLogic;
        private TwoHandScaleLogic scaleLogic;
        private TwoHandRotateLogic rotateLogic;

        #endregion Transform Info

        /// <summary>
        /// Maps input id -> position of hand.
        /// </summary>
        private readonly Dictionary<uint, Vector3> handsPressedLocationsMap = new Dictionary<uint, Vector3>();

        /// <summary>
        /// Maps input id -> input source. Then obtain position of input source using currentInputSource.TryGetGripPosition(currentInputSourceId, out inputPosition);
        /// </summary>
        private readonly Dictionary<uint, IMixedRealityInputSource> handsPressedInputSourceMap = new Dictionary<uint, IMixedRealityInputSource>();

        /// <summary>
        /// Change the manipulation mode.
        /// </summary>
        [System.Obsolete("Use ManipulationMode.")]
        public void SetManipulationMode(TwoHandedManipulationType mode)
        {
            manipulationMode = mode;
        }

        private void Awake()
        {
            moveLogic = new TwoHandMoveLogic();
            rotateLogic = new TwoHandRotateLogic(rotationConstraint);
            scaleLogic = new TwoHandScaleLogic();
        }

        private void Start()
        {
            if (hostTransform == null)
            {
                hostTransform = transform;
            }
        }

        private void Update()
        {
            // Update positions of all hands
            foreach (var key in handsPressedInputSourceMap.Keys)
            {
                var inputSource = handsPressedInputSourceMap[key];
                Vector3 inputPosition;
                if (inputSource.Pointers[0].TryGetPointerPosition(out inputPosition))
                {
                    handsPressedLocationsMap[key] = inputPosition;
                }
            }

            if (currentState != TwoHandedManipulationType.None)
            {
                UpdateStateMachine();
            }
        }

        private Vector3 GetInputPosition(InputEventData eventData)
        {
            Vector3 result;
            eventData.InputSource.Pointers[0].TryGetPointerPosition(out result);
            return result;
        }

        private void RemoveSourceIdFromHandMap(uint sourceId)
        {
            if (handsPressedLocationsMap.ContainsKey(sourceId))
            {
                handsPressedLocationsMap.Remove(sourceId);
            }

            if (handsPressedInputSourceMap.ContainsKey(sourceId))
            {
                handsPressedInputSourceMap.Remove(sourceId);
            }
        }

        /// <summary>
        /// Event Handler receives input from inputSource.
        /// </summary>
        public void OnInputDown(InputEventData eventData)
        {
            // Add to hand map
            handsPressedLocationsMap[eventData.SourceId] = GetInputPosition(eventData);
            handsPressedInputSourceMap[eventData.SourceId] = eventData.InputSource;
            UpdateStateMachine();
            eventData.Use();
        }

        /// <summary>
        /// Event Handler receives input from inputSource.
        /// </summary>
        public void OnInputUp(InputEventData eventData)
        {
            RemoveSourceIdFromHandMap(eventData.SourceId);
            UpdateStateMachine();
            eventData.Use();
        }

        /// <summary>
        /// OnSourceDetected Event Handler.
        /// </summary>
        public void OnSourceDetected(SourceStateEventData eventData) { }

        /// <summary>
        /// OnSourceLost Event Handler.
        /// </summary>
        public void OnSourceLost(SourceStateEventData eventData)
        {
            RemoveSourceIdFromHandMap(eventData.SourceId);
            UpdateStateMachine();
            eventData.Use();
        }

        /// <summary>
        /// Updates the state machine based on the current state and anything that might have changed with the hands.
        /// </summary>
        private void UpdateStateMachine()
        {
            var handsPressedCount = handsPressedLocationsMap.Count;
            TwoHandedManipulationType newState = currentState;

            switch (currentState)
            {
                case TwoHandedManipulationType.None:
                case TwoHandedManipulationType.Move:
                    if (handsPressedCount == 0)
                    {
                        newState = TwoHandedManipulationType.None;
                    }
                    else if (handsPressedCount == 1)
                    {
                        newState = enableOneHandMovement ? TwoHandedManipulationType.Move : TwoHandedManipulationType.None;
                    }
                    else if (handsPressedCount > 1)
                    {
                        newState = manipulationMode;
                    }
                    break;
                case TwoHandedManipulationType.Scale:
                case TwoHandedManipulationType.Rotate:
                case TwoHandedManipulationType.MoveScale:
                case TwoHandedManipulationType.MoveRotate:
                case TwoHandedManipulationType.RotateScale:
                case TwoHandedManipulationType.MoveRotateScale:
                    if (handsPressedCount == 0)
                    {
                        newState = TwoHandedManipulationType.None;
                    }
                    else if (handsPressedCount == 1)
                    {
                        newState = enableOneHandMovement ? TwoHandedManipulationType.Move : TwoHandedManipulationType.None;
                    }
                    break;
            }

            InvokeStateUpdateFunctions(currentState, newState);
            currentState = newState;
        }

        private void InvokeStateUpdateFunctions(TwoHandedManipulationType oldState, TwoHandedManipulationType newState)
        {
            if (newState != oldState)
            {
                switch (newState)
                {
                    case TwoHandedManipulationType.None:
                        OnManipulationEnded();
                        break;
                    case TwoHandedManipulationType.Move:
                        OnOneHandMoveStarted();
                        break;
                    case TwoHandedManipulationType.Scale:
                    case TwoHandedManipulationType.Rotate:
                    case TwoHandedManipulationType.MoveScale:
                    case TwoHandedManipulationType.MoveRotate:
                    case TwoHandedManipulationType.RotateScale:
                    case TwoHandedManipulationType.MoveRotateScale:
                        OnTwoHandManipulationStarted(newState);
                        break;
                }

                switch (oldState)
                {
                    case TwoHandedManipulationType.None:
                        OnManipulationStarted();
                        break;
                    case TwoHandedManipulationType.Move:
                        break;
                    case TwoHandedManipulationType.Scale:
                    case TwoHandedManipulationType.Rotate:
                    case TwoHandedManipulationType.MoveScale:
                    case TwoHandedManipulationType.MoveRotate:
                    case TwoHandedManipulationType.RotateScale:
                    case TwoHandedManipulationType.MoveRotateScale:
                        OnTwoHandManipulationEnded();
                        break;
                }
            }
            else
            {
                switch (newState)
                {
                    case TwoHandedManipulationType.None:
                        break;
                    case TwoHandedManipulationType.Move:
                        OnOneHandMoveUpdated();
                        break;
                    case TwoHandedManipulationType.Scale:
                    case TwoHandedManipulationType.Rotate:
                    case TwoHandedManipulationType.MoveScale:
                    case TwoHandedManipulationType.MoveRotate:
                    case TwoHandedManipulationType.RotateScale:
                    case TwoHandedManipulationType.MoveRotateScale:
                        OnTwoHandManipulationUpdated();
                        break;
                }
            }
        }

        private void OnTwoHandManipulationUpdated()
        {
#if UNITY_2017_2_OR_NEWER
            var targetRotation = hostTransform.rotation;
            var targetPosition = hostTransform.position;
            var targetScale = hostTransform.localScale;

            if ((currentState & TwoHandedManipulationType.Move) > 0)
            {
                targetPosition = moveLogic.Update(GetHandsCentroid());
            }

            if ((currentState & TwoHandedManipulationType.Rotate) > 0)
            {
                targetRotation = rotateLogic.Update(handsPressedLocationsMap, targetRotation);
            }

            if ((currentState & TwoHandedManipulationType.Scale) > 0)
            {
                targetScale = scaleLogic.Update(handsPressedLocationsMap);
            }

            hostTransform.position = targetPosition;
            hostTransform.rotation = targetRotation;
            hostTransform.localScale = targetScale;
#endif // UNITY_2017_2_OR_NEWER
        }

        private void OnOneHandMoveUpdated()
        {
            var targetPosition = moveLogic.Update(handsPressedLocationsMap.Values.First());

            hostTransform.position = targetPosition;
        }

        private void OnTwoHandManipulationEnded()
        {
#if UNITY_2017_2_OR_NEWER
            // This implementation currently does nothing
#endif // UNITY_2017_2_OR_NEWER
        }

        private Vector3 GetHandsCentroid()
        {
            Vector3 result = handsPressedLocationsMap.Values.Aggregate(Vector3.zero, (current, state) => current + state);
            return result / handsPressedLocationsMap.Count;
        }

        private void OnTwoHandManipulationStarted(TwoHandedManipulationType newState)
        {
#if UNITY_2017_2_OR_NEWER
            if ((newState & TwoHandedManipulationType.Rotate) > 0)
            {
                rotateLogic.Setup(handsPressedLocationsMap);
            }

            if ((newState & TwoHandedManipulationType.Move) > 0)
            {
                moveLogic.Setup(GetHandsCentroid(), hostTransform);
            }

            if ((newState & TwoHandedManipulationType.Scale) > 0)
            {
                scaleLogic.Setup(handsPressedLocationsMap, hostTransform);
            }
#endif // UNITY_2017_2_OR_NEWER
        }

        private void OnOneHandMoveStarted()
        {
            Assert.IsTrue(handsPressedLocationsMap.Count == 1);

            moveLogic.Setup(handsPressedLocationsMap.Values.First(), hostTransform);
        }

        private void OnManipulationStarted()
        {
            InputSystem.PushModalInputHandler(gameObject);
        }

        private void OnManipulationEnded()
        {
            InputSystem.PopModalInputHandler();
        }

        public void OnInputPressed(InputEventData<float> eventData)
        {
        }

        public void OnPositionInputChanged(InputEventData<Vector2> eventData)
        {
        }
    }
}
