// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace Microsoft.MixedReality.Toolkit.Input
{
    public class HandInteractionPan : BaseFocusHandler, IMixedRealityTouchHandler, IMixedRealityInputHandler
    {
        private enum TouchType
        {
            IndexFinger = 0,
            HandRay
        }


        #region Serialized Fields
        [Header("Settings")]
        [SerializeField]
        private TouchType touchType = TouchType.IndexFinger;
        [SerializeField]
        [Tooltip("Each object listed must have a script that implements the IHandPanHandler interface or it will not receive events")]
        private GameObject[] panEventReceivers = null;
        [SerializeField]
        [FormerlySerializedAs("enabled")]
        private bool isEnabled = true;
        [SerializeField]
        private GameObject touchVisual = null;
        public bool Enabled { get => isEnabled; set => isEnabled = value; }


        [Header("Behavior")]
        [SerializeField]
        private bool lockHorizontal = false;
        [SerializeField]
        private bool lockVertical = false;
        [SerializeField]
        [Tooltip("If this is checked, Max Pan Horizontal and Max Pan Vertical are ignored.")]
        private bool unlimitedPan = true;
        [SerializeField]
        [Range(1.0f, 20.0f)]
        private float maxPanHorizontal = 2;
        [SerializeField]
        [Range(1.0f, 20.0f)]
        private float maxPanVertical = 2;
        [SerializeField]
        [Range(0.0f, 0.99f)]
        [Tooltip("a value of 0 results in panning coming to a complete stop when released.")]
        private float momentumHorizontal = 0.9f;
        [SerializeField]
        [Tooltip("a value of 0 results in panning coming to a complete stop when released.")]
        [Range(0.0f, 0.99f)]
        private float momentumVertical = 0.9f;
        [SerializeField]
        private bool springActive = false;
        #endregion Serialized Fields


        #region Private Properties
        private List<Vector2> uvs = new List<Vector2>();
        private List<Vector2> uvsTouchStart = new List<Vector2>();
        private MeshFilter meshFilter;
        private Mesh mesh;
        private Vector2 uvOffset = Vector2.zero;
        private Vector2 uvTotalOffset = Vector2.zero;
        private IMixedRealityInputSource touchingSource;
        private Vector3 touchingInitialPt;
        private Vector3 touchingPoint;
        private Vector2 touchingUV;
        private Vector2 touchingInitialUV;
        private Vector2 touchingUVOffset;
        private Vector2 touchingUVTotalOffset;
        private Material currentMaterial;
        private Renderer visualRenderer;
        private List<IMixedRealityHandPanHandler> handlerInterfaces = new List<IMixedRealityHandPanHandler>();
        private IMixedRealityController currentController = null;
        private bool TouchActive
        {
            get
            {
                return touchingPoint != Vector3.zero;
            }
        }
        #endregion Private Properties


        #region MonoBehaviour Handlers
        private void Awake()
        {
            Initialize();
        }
        private void Update()
        {
            if (isEnabled == false)
            {
                return;
            }

            if (currentController != null)
            {
                if (touchType == TouchType.IndexFinger)
                {
                    if (true == TryGetHandPoint(currentController, TrackedHandJoint.IndexTip, out Vector3 tryHandPoint))
                    {
                        touchingPoint = SnapFingerToQuad(tryHandPoint);
                    }
                    else
                    {
                        return;
                    }
                }
                else if (touchType == TouchType.HandRay)
                {
                    if (true == TryGetHandPoint(currentController, TrackedHandJoint.Palm, out Vector3 tryHandPoint))
                    {
                        touchingPoint = SnapFingerToQuad(tryHandPoint);
                    }
                    else
                    {
                        return;
                    }
                }

                MoveTouch();

                if (visualRenderer)
                {
                    touchVisual.transform.position = touchingPoint;
                    visualRenderer.enabled = true;
                }
            }
            else
            {
                if (visualRenderer)
                {
                    visualRenderer.enabled = false;
                }
            }

            UpdateIdle();
            UpdateUVMapping();
        }
        #endregion MonoBehaviour Handlers


        #region Private Methods
        private bool TryGetHandPoint(IMixedRealityController controller, TrackedHandJoint joint, out Vector3 handPoint)
        {
            Vector3 point = Vector3.zero;
            if (true == HandJointUtils.TryGetJointPose(joint, controller.ControllerHandedness, out MixedRealityPose pose))
            {
                handPoint = pose.Position;
                return true;
            }

            handPoint = point;
            return false;
        }
        private void Initialize()
        {
            //check for box collider
            Collider collider = GetComponent<Collider>();
            if (collider == null)
            {
                Debug.Log("The GameObject that runs this script must have a Collider attached.");
            }
            else
            {
                this.GetComponent<Renderer>().material.mainTexture.wrapMode = TextureWrapMode.Repeat;
            }

            currentMaterial = this.gameObject.GetComponent<Renderer>().material;

            //cache renderer for visual
            if (touchVisual != null)
            {
                visualRenderer = touchVisual.GetComponent<Renderer>();
                visualRenderer.enabled = true;
            }

            //get event targets
            foreach (GameObject gameObject in panEventReceivers)
            {
                if (gameObject != null)
                {
                    IMixedRealityHandPanHandler handler = gameObject.GetComponent<IMixedRealityHandPanHandler>();
                    if (handler != null)
                    {
                        handlerInterfaces.Add(handler);
                    }
                }
            }

            //precache references
            meshFilter = gameObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                Debug.Log("The GameObject: " + this.gameObject.name + " " + "does not have a Mesh component.");
            }
            else
            {
                mesh = meshFilter.mesh;
            }
        }
        private IMixedRealityHandPanHandler[] GetInterfaces()
        {
            List<IMixedRealityHandPanHandler> interfaces = new List<IMixedRealityHandPanHandler>();
            GameObject[] gameObjects = SceneManager.GetActiveScene().GetRootGameObjects();

            foreach (var gameObject in gameObjects)
            {
                IMixedRealityHandPanHandler[] childrenInterfaces = gameObject.GetComponentsInChildren<IMixedRealityHandPanHandler>();
                foreach (var childInterface in childrenInterfaces)
                {
                    interfaces.Add(childInterface);
                }
            }

            return interfaces.ToArray();

        }
        private void UpdateIdle()
        {
            if (TouchActive == false)
            {
                if (Mathf.Abs(uvOffset.x) < 0.01f && Mathf.Abs(uvOffset.y) < 0.01f)
                {
                    uvOffset = Vector2.zero;
                }
                else
                {
                    uvOffset = new Vector2(uvOffset.x * momentumHorizontal, uvOffset.y * momentumVertical);
                    FirePanning(0);

                    if (springActive == true)
                    {
                        List<Vector2> uvCurrent = new List<Vector2>();
                        mesh.GetUVs(0, uvCurrent);
                        uvOffset = -(uvsTouchStart[0] - uvCurrent[0]) * 0.1f;
                        FirePanning(0);
                    }
                }
            }
        }
        private void UpdateTouchPoint()
        {
            Vector2 newUV = GetUVFromPoint(touchingPoint);

            uvOffset = newUV - touchingUV;
            uvOffset.y = -uvOffset.y;
            touchingUV = newUV;
        }
        private void UpdateUVMapping()
        {
            Vector2 tiling = currentMaterial != null ? currentMaterial.mainTextureScale : new Vector2(1.0f, 1.0f);
            List<Vector2> uvs = new List<Vector2>();
            List<Vector2> uvsOrig = new List<Vector2>();

            Vector2 uvTestValue;
            mesh.GetUVs(0, uvs);
            uvsOrig.AddRange(uvs);

            //test for pan limits
            if (unlimitedPan == false)
            {
                bool xLimited = false;
                bool yLimited = false;
                for (int i = 0; i < uvs.Count; ++i)
                {
                    uvTestValue = uvs[i] - uvOffset;
                    if (uvTestValue.x > tiling.x * maxPanHorizontal || uvTestValue.x < -(tiling.x * maxPanHorizontal))
                    {
                        xLimited = true;
                    }
                    if (uvTestValue.y > tiling.y * maxPanVertical || uvTestValue.y < -(tiling.y * maxPanVertical))
                    {
                        yLimited = true;
                    }
                }

                for (int i = 0; i < uvs.Count; ++i)
                {
                    uvs[i] = new Vector2(xLimited ? uvs[i].x : uvs[i].x - uvOffset.x, yLimited ? uvs[i].y : uvs[i].y - uvOffset.y);
                }
            }
            else
            {
                for (int i = 0; i < uvs.Count; ++i)
                {
                    uvs[i] -= uvOffset;
                }
            }
            mesh.uv = uvs.ToArray();













            ////adjust all uv coordinates using uvOffset value
            //bool oobX = false;
            //bool oobY = false;
            //for (int i = 0; i < uvs.Count; ++i)
            //{
            //    //this is the key line
            //    uvs[i] -= uvOffset;

            //    if (wrapTexture == false)
            //    {
            //        if (uvs[i].x > (tiling.x) || uvs[i].x <= -0.001f)
            //        {
            //            oobX = true;
            //            uvOffset.x = 0.0f;
            //        }

            //        if (uvs[i].y > (tiling.y) || uvs[i].y <= -0.001f)
            //        {
            //            oobY = true;
            //            uvOffset.y = 0.0f;
            //        }
            //    }
            //}

            //for (int i = 0; i < uvs.Count; ++i)
            //{
            //    uvs[i] = new Vector2(oobX ? uvsOrig[i].x : uvs[i].x, oobY ? uvsOrig[i].y : uvs[i].y);
            //}

            //mesh.uv = uvs.ToArray();
        }
        private Vector2 GetUVFromPoint(Vector3 point)
        {
            Vector2 uv = GetPlanePoint(point);
            uv = new Vector2(lockHorizontal ? 0.0f : uv.x, lockVertical ? 0.0f : uv.y);
            return uv;
        }
        private Vector2 GetPlanePoint(Vector3 point)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3 upperLeft = transform.TransformPoint(vertices[3]);
            Vector3 upperRight = transform.TransformPoint(vertices[1]);//lower left
            Vector3 lowerLeft = transform.TransformPoint(vertices[0]);

            float magVertical = (lowerLeft - upperLeft).magnitude;
            float magHorizontal = (upperRight - upperLeft).magnitude;

            float v = Vector3.Dot(point - upperLeft, (lowerLeft - upperLeft) / magVertical);
            float h = Vector3.Dot(point - upperLeft, (upperRight - upperLeft) / magHorizontal);
            v /= magVertical;
            h /= magHorizontal;
            return new Vector2(h, v);
        }
        private Vector3 SnapFingerToQuad(Vector3 pointToSnap)
        {
            Vector3 planePoint = this.transform.TransformPoint(mesh.vertices[0]);
            Vector3 planeNormal = gameObject.transform.forward;

            return Vector3.ProjectOnPlane(pointToSnap - planePoint, planeNormal) + planePoint;
        }
        private void DisconnectTouch()
        {
            currentController = null;
            touchingSource = null;
            touchingPoint = Vector3.zero;
            touchingInitialPt = Vector3.zero;
            touchingUV = Vector2.zero;
            touchingInitialUV = Vector2.zero;
            touchingUVOffset = Vector2.zero;
            EndTouch();
        }
        #endregion Private Methods


        #region Internal State Handlers
        private void StartTouch()
        {
            UpdateTouchPoint();
            mesh.GetUVs(0, uvsTouchStart);
            FirePanStarted(touchingSource.SourceId);
        }
        private void EndTouch()
        {
            touchingUVTotalOffset += uvOffset;
            FirePanEnded(0);
        }
        private void MoveTouch()
        {
            UpdateTouchPoint();
            FirePanning(touchingSource.SourceId);
        }
        #endregion Internal State Handlers


        #region Fire Events to Listening Objects
        private void FirePanStarted(uint id)
        {
            HandPanEventData eventData = new HandPanEventData(EventSystem.current);
            eventData.Initialize(touchingSource, uvOffset);

            foreach (IMixedRealityHandPanHandler handler in handlerInterfaces)
            {
                if (handler != null)
                {
                    handler.OnPanStarted(eventData);
                }
            }
        }
        private void FirePanEnded(uint id)
        {
            HandPanEventData eventData = new HandPanEventData(EventSystem.current);
            eventData.Initialize(null, Vector2.zero);

            foreach (IMixedRealityHandPanHandler handler in handlerInterfaces)
            {
                if (handler != null)
                {
                    handler.OnPanEnded(eventData);
                }
            }
        }
        private void FirePanning(uint id)
        {
            HandPanEventData eventData = new HandPanEventData(EventSystem.current);
            eventData.Initialize(touchingSource, uvOffset);

            foreach (IMixedRealityHandPanHandler handler in handlerInterfaces)
            {
                if (handler != null)
                {
                    handler.OnPanning(eventData);
                }
            }
        }
        #endregion Fire Events to Listening Objects


        #region IMixedRealityHandTrackHandler
        public void OnTouchStarted(HandTrackingInputEventData eventData)
        {
            if (touchType == TouchType.IndexFinger)
            {
                if (touchingSource == null)
                {
                    touchingSource = eventData.InputSource;
                    currentController = touchingSource.Pointers[0].Controller;

                    TryGetHandPoint(eventData.Controller, TrackedHandJoint.IndexTip, out Vector3 handPt);

                    touchingPoint = SnapFingerToQuad(handPt);
                    touchingInitialPt = touchingPoint;
                    touchingUV = GetUVFromPoint(touchingPoint);
                    touchingInitialUV = touchingUV;
                    touchingUVOffset = touchingUVTotalOffset;

                    StartTouch();

                    eventData.Use();
                }
            }
        }
        public void OnTouchCompleted(HandTrackingInputEventData eventData)
        {
            if (touchType == TouchType.IndexFinger)
            {
                if (touchingSource == eventData.InputSource)
                {
                    DisconnectTouch();
                    eventData.Use();
                }
            }
        }
        public void OnTouchUpdated(HandTrackingInputEventData eventData) { }
        #endregion IMixedRealityHandTrackHandler


        #region BaseFocusHandler Methods
        public override void OnFocusEnter(FocusEventData eventData) { }
        public override void OnFocusExit(FocusEventData eventData)
        {
            DisconnectTouch();
        }
        #endregion


        #region IMixedRealityInputHandler Methods
        public void OnInputDown(InputEventData eventData)
        {
            if (touchType == TouchType.HandRay)
            {
                if (touchingSource == null)
                {
                    touchingSource = eventData.InputSource;
                    currentController = touchingSource.Pointers[0].Controller;
                    TryGetHandPoint(currentController, TrackedHandJoint.Palm, out touchingPoint);
                    touchingPoint = SnapFingerToQuad(touchingPoint);
                    touchingInitialPt = touchingPoint;
                    touchingUV = GetUVFromPoint(touchingPoint);
                    touchingInitialUV = touchingUV;
                    touchingUVOffset = touchingUVTotalOffset;

                    StartTouch();

                    eventData.Use();
                }
            }
        }
        public void OnInputUp(InputEventData eventData)
        {
            if (touchType == TouchType.HandRay)
            {
                if (touchingSource == eventData.InputSource)
                {
                    DisconnectTouch();
                    eventData.Use();
                }
            }
        }
        public void OnPositionInputChanged(InputEventData<Vector2> eventData) { }
        public void OnInputPressed(InputEventData<float> eventData) { }
        #endregion IMixedRealityInputHandler Methods
    }
}