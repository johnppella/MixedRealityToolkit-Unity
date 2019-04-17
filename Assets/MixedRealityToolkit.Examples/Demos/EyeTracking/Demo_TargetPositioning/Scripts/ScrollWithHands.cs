using Microsoft.MixedReality.Toolkit.Core.Interfaces.Devices;
using System.Collections.Generic;
using Microsoft.MixedReality.Toolkit.Input;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using Microsoft.MixedReality.Toolkit.Utilities;

namespace Microsoft.MixedReality.Toolkit.Examples.Demos.EyeTracking
{
    public class ScrollWithHands : BaseFocusHandler, IMixedRealityInputHandler, IMixedRealitySourceStateHandler
    {
        private class HandData
        {
            public IMixedRealityController controller;
            public Vector3 grabPointOffset;
        }

        #region Serialized Properties
        [Header("Properties")]
        [SerializeField]
        private GameObject rail = null;
        [SerializeField]
        private float scrollMaxDistance;
        [SerializeField]
        private float scrollMinDistance;
        [SerializeField]
        private float scrollMinValue = -0.3f;
        [SerializeField]
        private float scrollMaxValue = 0.3f;
        [SerializeField]
        private Vector2 LocalMinMax_X = new Vector2(float.NegativeInfinity, float.PositiveInfinity);
        [SerializeField]
        private Vector2 LocalMinMax_Y = new Vector2(float.NegativeInfinity, float.PositiveInfinity);
        [SerializeField]
        private Vector2 LocalMinMax_Z = new Vector2(float.NegativeInfinity, float.PositiveInfinity);
        [SerializeField]
        private bool useTransparencyWhenScrolling = true;
        [SerializeField]
        [Range(0.0f, 1.0f)]
        private float transparencyWhenScrolling = 0.5f;
        [SerializeField]
        private bool useColorWhenScrolling = true;
        [SerializeField]
        private Color colorWhenScrolling = new Color(1, 0, 0);
        [SerializeField]
        private TextMesh txtOutput_sliderValue = null;

        [Header("Audio Feedback")]
        [SerializeField]
        private AudioClip audio_OnDragStart = null;

        [SerializeField]
        private AudioClip audio_OnDragStop = null;

        [Header("Events")]
        [SerializeField]
        private UnityEvent OnDragStart = null;
        [SerializeField]
        private UnityEvent OnDragEnd = null;
        #endregion Serialized Properties

        #region Private Properties
        private Dictionary<uint, HandData> handDataMap = new Dictionary<uint, HandData>();
        private AudioSource dragStartAudioSource;
        private AudioSource dragEndAudioSource;
        private float nonDraggingAlpha = 1.0f;
        private Color nonDraggingColor = new Color(1, 1, 1);
        #endregion Private Properties

        #region Monobehaviour Event Handlers
        private void Awake()
        {
            if (audio_OnDragStart != null)
            {
                dragStartAudioSource = this.gameObject.AddComponent<AudioSource>();
                dragStartAudioSource.clip = audio_OnDragStart;

                dragEndAudioSource = this.gameObject.AddComponent<AudioSource>();
                dragEndAudioSource.clip = audio_OnDragStop;
            }
            Material mat = gameObject.GetComponent<Renderer>().material;
            nonDraggingColor = mat.color;
            nonDraggingAlpha = mat.color.a;

            if (useTransparencyWhenScrolling == false)
            {
                transparencyWhenScrolling = nonDraggingAlpha;
            }
            if (useColorWhenScrolling == false)
            {
                colorWhenScrolling = nonDraggingColor;
            }
        }
        private void Start()
        {
            Vector3 extents = rail.transform.localScale;
            scrollMaxDistance = Mathf.Max(Mathf.Max(extents.x, extents.y), extents.z);
            scrollMinDistance = -scrollMaxDistance;
        }
        private void Update()
        {
            if (handDataMap.Keys.Count > 0)
            {
                foreach (uint key in handDataMap.Keys)
                {
                    HandData data = handDataMap[key];
                    Vector3 newPoint = GetConstrainedPosition(GetGrabPosition(data.controller) + handDataMap[key].grabPointOffset);
                    this.gameObject.transform.position = newPoint;
                    UpdateOutputLabel();
                }
            }
        }
        #endregion Monobehaviour Event Handlers

        #region Private Methods
        private Vector3 GetConstrainedPosition(Vector3 position)
        {
            Vector3 right = gameObject.transform.right;
            float distance = Vector3.Dot(position - rail.transform.position, right);
            distance = distance < 0 ? Mathf.Max(distance, scrollMinDistance) : distance;
            distance = distance > 0 ? Mathf.Min(distance, scrollMaxDistance) : distance;

            return rail.transform.position + (right * distance);
        }
        private Vector3 GetGrabPosition(IMixedRealityController controller)
        {
            HandJointUtils.TryGetJointPose(TrackedHandJoint.IndexTip, controller.ControllerHandedness, out MixedRealityPose pose);
            return pose.Position;
        }
        private void UpdateOutputLabel()
        {
            if (txtOutput_sliderValue != null)
            {
                float range = (gameObject.transform.localPosition.x - LocalMinMax_X.x) / (LocalMinMax_X.y - LocalMinMax_X.x);
                txtOutput_sliderValue.text = $"{scrollMinValue + (range * (scrollMaxValue - scrollMinValue)): 0.00}";
            }
        }
        private void SetDragColorAndAlpha(bool isScrolling)
        {
            Material mat = gameObject.GetComponent<Renderer>().material;
            Color newColor = new Color(isScrolling ? colorWhenScrolling.r : nonDraggingColor.r,
                                        isScrolling ? colorWhenScrolling.g : nonDraggingColor.g,
                                        isScrolling ? colorWhenScrolling.b : nonDraggingColor.b,
                                        isScrolling ? transparencyWhenScrolling : nonDraggingAlpha);
            mat.color = newColor;
            ChangeRenderMode.ChangeRenderModes(mat, newColor.a < 1 ? ChangeRenderMode.BlendMode.Fade : ChangeRenderMode.BlendMode.Opaque);
        }

        private void OnDragStarted()
        {
            if (audio_OnDragStart != null)
            {
                dragStartAudioSource.Play();
            }

            SetDragColorAndAlpha(true);

            OnDragStart.Invoke();
        }
        private void OnDragEnded()
        {
            if (audio_OnDragStop != null)
            {
                dragEndAudioSource.Play();
            }

            SetDragColorAndAlpha(false);

            OnDragEnd.Invoke();
        }
        #endregion Private Methods


        #region IMixedRealityInputHandler Event Handlers
        public void OnInputDown(InputEventData eventData)
        {
            if (eventData.MixedRealityInputAction.Description == "Grip Press")
            {
                if (handDataMap.Keys.Count == 0)
                {
                    HandData data = new HandData();
                    data.controller = eventData.InputSource.Pointers[0].Controller;
                    data.grabPointOffset = gameObject.transform.position - GetGrabPosition(data.controller);
                    handDataMap.Add(eventData.SourceId, data);
                    OnDragStarted();
                }
            }
        }
        public void OnInputUp(InputEventData eventData)
        {
            if (handDataMap.ContainsKey(eventData.SourceId) == true)
            {
                handDataMap.Remove(eventData.SourceId);
                OnDragEnded();
            }
        }
        #endregion IMixedRealityInputHandler Event Handlers

        #region IMixedRealitySourceStateHandler
        public void OnSourceDetected(SourceStateEventData eventData) { }
        public void OnSourceLost(SourceStateEventData eventData)
        {
            if (handDataMap.ContainsKey(eventData.SourceId) == true)
            {
                handDataMap.Remove(eventData.SourceId);
                OnDragEnded();
            }
        }
        #endregion IMixedRealitySourceStateHandler
    }
}
