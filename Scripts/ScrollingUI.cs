using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Elanetic.UI.Unity
{
    public class ScrollingUI : MonoBehaviour, IScrollHandler
    {
        public RectTransform content;

        public bool horizontal = false;
        public bool vertical = true;
        public float horizontalPercent
        {
            get => m_HorizontalPercent;
            set
            {
                m_HorizontalPercent = Mathf.Clamp01(value);
                if(m_HorizontalScrollbar != null)
                {
                    m_HorizontalScrollbar.SetValueWithoutNotify(m_HorizontalPercent);
                }
            }
        }
        public float verticalPercent
        {
            get => m_VerticalPercent;
            set
            {
                m_VerticalPercent = Mathf.Clamp01(value);
                if(m_VerticalScrollbar != null)
                {
                    m_VerticalScrollbar.SetValueWithoutNotify(m_VerticalPercent);
                }
            }
        }

        public float scrollSensitivity = 1.0f;

        //If enabled vertical scrolling on input devices such as the scroll wheel will allow for horizontal only scrolling UIs to scroll.
        //Specfically scrolling up on the mouse wheel will scroll this UI left and scrolling down on the mouse wheel will scroll this UI right.
        //This will not work if vertical property is enabled.
        public bool verticalInputScrollsHorizontal = false;

        public Scrollbar horizontalScrollbar 
        { 
            get => m_HorizontalScrollbar;
            set
            {
                if(m_HorizontalScrollbar != null)
                {
                    m_HorizontalScrollbar.onValueChanged.RemoveListener(OnHorizontalScrollBarMoved);
                }
                m_HorizontalScrollbar = value;
                if(m_HorizontalScrollbar != null)
                {
                    m_HorizontalScrollbar.onValueChanged.AddListener(OnHorizontalScrollBarMoved);
                    m_HorizontalScrollbar.SetValueWithoutNotify(horizontalPercent);
                }
            }
        }
        public Scrollbar verticalScrollbar
        {
            get => m_VerticalScrollbar;
            set
            {
                if(m_VerticalScrollbar != null)
                {
                    m_VerticalScrollbar.onValueChanged.RemoveListener(OnVerticalScrollBarMoved);
                }
                m_VerticalScrollbar = value;
                if(m_VerticalScrollbar != null)
                {
                    m_VerticalScrollbar.onValueChanged.AddListener(OnVerticalScrollBarMoved);
                    m_VerticalScrollbar.SetValueWithoutNotify(verticalPercent);
                }
            }
        }

        public float minScrollBarSize { get; set; } = 0.025f;

        public RectTransform rectTransform { get; private set; }

        [SerializeField]
        [Range(0, 1)]
        private float m_HorizontalPercent = 0.0f;
        [SerializeField]
        [Range(0, 1)]
        private float m_VerticalPercent = 0.0f;

        [SerializeField]
        private Scrollbar m_HorizontalScrollbar;
        [SerializeField]
        private Scrollbar m_VerticalScrollbar;

        private Vector2 m_LastViewPortSize = Vector2.zero;
        private Vector2 m_LastContentSize = Vector2.zero;

        void Awake()
        {
            rectTransform = (RectTransform)transform;
            if(m_HorizontalScrollbar != null)
            {
                m_HorizontalScrollbar.onValueChanged.AddListener(OnHorizontalScrollBarMoved);
            }
            if(m_VerticalScrollbar != null)
            {
                m_VerticalScrollbar.onValueChanged.AddListener(OnVerticalScrollBarMoved);
            }

            if(content == null)
            {
                GameObject contentObject = new GameObject("Content");
                content = contentObject.AddComponent<RectTransform>();
                content.SetParent(transform);
                content.localScale = Vector3.one;
                content.rotation = Quaternion.identity;
                content.anchorMin = Vector2.zero;
                content.anchorMax = Vector2.one;
                content.offsetMin = Vector2.zero;
                content.offsetMax = Vector2.zero;
            }
        }

        void Update()
        {
            if(content == null) return;

            Vector2 newPosition = content.localPosition;
            if(vertical)
            {
                newPosition = new Vector2(newPosition.x, -(rectTransform.pivot.y * rectTransform.rect.size.y));
                float percent = 1.0f - verticalPercent;
                if(content.rect.height <= rectTransform.rect.height)
                {
                    percent = 1.0f;
                }

                newPosition += new Vector2(0.0f, ((rectTransform.rect.height - content.rect.height) * percent) + (content.rect.height * content.pivot.y));

            }
            if(horizontal)
            {
                newPosition = new Vector2(-(rectTransform.pivot.x * rectTransform.rect.size.x), newPosition.y);
                float percent = horizontalPercent;
                if(content.rect.width <= rectTransform.rect.width)
                {
                    percent = 0.0f;
                }
                newPosition += new Vector2(((rectTransform.rect.width - content.rect.width) * percent) + (content.rect.width * content.pivot.x), 0.0f);
            }

            content.localPosition = newPosition;

            Vector2 contentSize = content.rect.size;
            Vector2 viewportSize = rectTransform.rect.size;
            if(m_LastContentSize != contentSize || m_LastViewPortSize != viewportSize)
            {
                OnContentSizeChanged();
                m_LastContentSize = contentSize;
                m_LastViewPortSize = viewportSize;
            }
        }

        public void OnScroll(PointerEventData eventData)
        {
            if(vertical)
            {
                float differenceY = content.rect.height - rectTransform.rect.height;
                if(differenceY > 0)
                {
                    float scrollAmountY = (eventData.scrollDelta.y * scrollSensitivity) / differenceY;
                    verticalPercent -= scrollAmountY;
                }
            }

            if(horizontal)
            {
                float differenceX = content.rect.width - rectTransform.rect.width;
                if(differenceX > 0)
                {
                    float scrollAmountX = (eventData.scrollDelta.x * scrollSensitivity) / differenceX;
                    horizontalPercent -= scrollAmountX;
                }

                if(!vertical && verticalInputScrollsHorizontal)
                {
                    horizontalPercent -= (eventData.scrollDelta.y * scrollSensitivity) / differenceX;
                }
            }
        }

        private void OnContentSizeChanged()
        {

            if(m_HorizontalScrollbar != null)
            {
                if(content.rect.width <= 0.0f)
                {
                    m_HorizontalScrollbar.size = 1.0f;
                }
                else
                {
                    m_HorizontalScrollbar.size = Mathf.Clamp(rectTransform.rect.width / content.rect.width, minScrollBarSize, 1.0f);
                }
            }

            if(m_VerticalScrollbar != null)
            {
                if(content.rect.height <= 0.0f)
                {
                    m_VerticalScrollbar.size = 1.0f;
                }
                else
                {
                    m_VerticalScrollbar.size = Mathf.Clamp(rectTransform.rect.height / content.rect.height, minScrollBarSize, 1.0f);
                }
            }
        }

        private void OnHorizontalScrollBarMoved(float value)
        {
            horizontalPercent = value;
        }

        private void OnVerticalScrollBarMoved(float value)
        {
            verticalPercent = value;
        }
    }
}