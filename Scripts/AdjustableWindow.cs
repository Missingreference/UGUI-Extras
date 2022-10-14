
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using Elanetic.Native;
using Cursor = Elanetic.Native.Cursor;

namespace Elanetic.UI.Unity
{
    [RequireComponent(typeof(RectTransform))]
    public class AdjustableWindow : UIBehaviour, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerMoveHandler, IPointerEnterHandler, IPointerExitHandler
    {

        public Vector2 minSize
        {
            get => m_MinSize;
            set
            {
                m_MinSize = value;

                float targetWidth = Mathf.Max(rectTransform.rect.width, m_MinSize.x);
                float targetHeight = Mathf.Max(rectTransform.rect.height, m_MinSize.y);

                Vector2 parentSizes = Vector2.zero;
                RectTransform parentRectTransform = rectTransform.parent as RectTransform;
                if (rectTransform != null)
                {
                    parentSizes = rectTransform.rect.size;
                }
                rectTransform.sizeDelta = new Vector2(targetWidth - parentSizes.x * (rectTransform.anchorMax.x - rectTransform.anchorMin.x), targetHeight - parentSizes.y * (rectTransform.anchorMax.y - rectTransform.anchorMin.y));
            }
        }
        public float adjustEdgeSize
        {
            get => m_AdjustEdgeSize;
            set
            {
                m_AdjustEdgeSize = value;
                m_HalfAdjustEdgeSize = m_AdjustEdgeSize * 0.5f;

                //Ensure extra room for cursor adjusting
                if (m_TargetGraphic != null)
                {
                    m_TargetGraphic.raycastPadding = new Vector4(-m_HalfAdjustEdgeSize, -m_HalfAdjustEdgeSize, -m_HalfAdjustEdgeSize, -m_HalfAdjustEdgeSize);
                }
            }
        }
        public float topMoveSize
        {
            get => m_TopMoveSize;
            set
            {
                m_TopMoveSize = value;
            }
        }

        public float extraCornerAdjustSize { get; set; } = 16.0f;

        public RectTransform rectTransform => (RectTransform)transform;

        /// <summary>
        /// The RectTransform boundaries where the adjustable window must be visible within.
        /// </summary>
        public RectTransform draggableRect
        {
            get => m_DraggableRect;
            set
            {
                m_DraggableRect = value;
                if(m_DraggableRect != null)
                {
                    FixWindowViewAlignment();
                }
            }
        }

        private Vector2 m_MinSize = new Vector2(100.0f, 100.0f);
        private float m_AdjustEdgeSize = 10.0f;
        private float m_HalfAdjustEdgeSize = 5.0f;
        private float m_TopMoveSize = 10.0f;
        private Graphic m_TargetGraphic;
        private RectTransform m_DraggableRect = null;

        private AdjustWindowState m_State = AdjustWindowState.None;
        private enum AdjustWindowState
        {
            None,
            Top,
            Bottom,
            Left,
            Right,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
            Move
        }

        protected override void Awake()
        {
            //Add extra space to the raycast padding for cursor window size adjusting
            m_TargetGraphic = GetComponent<Graphic>();
            if(m_TargetGraphic != null)
            {
                m_TargetGraphic.raycastPadding = new Vector4(-m_HalfAdjustEdgeSize, -m_HalfAdjustEdgeSize, -m_HalfAdjustEdgeSize, -m_HalfAdjustEdgeSize);
            }

            draggableRect = transform.parent as RectTransform;
        }

        Vector2 startWindowPosition;

        void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, null, out Vector2 localPosition);

            Vector2 min = new Vector2(rectTransform.rect.x + m_HalfAdjustEdgeSize, rectTransform.rect.y + m_HalfAdjustEdgeSize);
            Vector2 max = new Vector2(rectTransform.rect.x + rectTransform.rect.width - m_HalfAdjustEdgeSize, rectTransform.rect.y + rectTransform.rect.height - m_HalfAdjustEdgeSize);

            if (localPosition.y >= max.y) //Top Edge
            {
                if(localPosition.x <= min.x + extraCornerAdjustSize)
                {
                    m_State = AdjustWindowState.TopLeft;
                    startWindowPosition = new Vector2(rectTransform.rect.width + rectTransform.rect.x + rectTransform.localPosition.x, rectTransform.rect.y + rectTransform.localPosition.y);
                }
                else if(localPosition.x >= max.x - extraCornerAdjustSize)
                {
                    m_State = AdjustWindowState.TopRight;
                    startWindowPosition = new Vector2(rectTransform.rect.x + rectTransform.localPosition.x, rectTransform.rect.y + rectTransform.localPosition.y);
                }
                else
                {
                    m_State = AdjustWindowState.Top;
                    startWindowPosition = new Vector2(0.0f, rectTransform.rect.y + rectTransform.localPosition.y);
                }
            }
            else if(localPosition.y <= min.y) //Bottom Edge
            {
                if(localPosition.x <= min.x + extraCornerAdjustSize)
                {
                    m_State = AdjustWindowState.BottomLeft;
                    startWindowPosition = new Vector2(rectTransform.rect.width + rectTransform.rect.x + rectTransform.localPosition.x, rectTransform.rect.height + rectTransform.rect.y + rectTransform.localPosition.y);
                }
                else if(localPosition.x >= max.x - extraCornerAdjustSize)
                {
                    m_State = AdjustWindowState.BottomRight;
                    startWindowPosition = new Vector2(rectTransform.rect.x + rectTransform.localPosition.x, rectTransform.rect.height + rectTransform.rect.y + rectTransform.localPosition.y);
                }
                else
                {
                    m_State = AdjustWindowState.Bottom;
                    startWindowPosition = new Vector2(0.0f, rectTransform.rect.height + rectTransform.rect.y + rectTransform.localPosition.y);
                }
            }
            else if(localPosition.x <= min.x) //Left Edge
            {
                if(localPosition.y <= min.y + extraCornerAdjustSize)
                {
                    m_State = AdjustWindowState.BottomLeft;
                    startWindowPosition = new Vector2(rectTransform.rect.width + rectTransform.rect.x + rectTransform.localPosition.x, rectTransform.rect.height + rectTransform.rect.y + rectTransform.localPosition.y);
                }
                else if (localPosition.y >= max.y - extraCornerAdjustSize)
                {
                    m_State = AdjustWindowState.TopLeft;
                    startWindowPosition = new Vector2(rectTransform.rect.width + rectTransform.rect.x + rectTransform.localPosition.x, rectTransform.rect.y + rectTransform.localPosition.y);
                }
                else
                {
                    m_State = AdjustWindowState.Left;
                    startWindowPosition = new Vector2(rectTransform.rect.width + rectTransform.rect.x + rectTransform.localPosition.x, 0.0f);
                }
            }
            else if(localPosition.x >= max.x) //Right Edge
            {
                if(localPosition.y <= min.y + extraCornerAdjustSize)
                {
                    m_State = AdjustWindowState.BottomRight;
                    startWindowPosition = new Vector2(rectTransform.rect.x + rectTransform.localPosition.x, rectTransform.rect.height + rectTransform.rect.y + rectTransform.localPosition.y);
                }
                else if(localPosition.y >= max.y - extraCornerAdjustSize)
                {
                    m_State = AdjustWindowState.TopRight;
                    startWindowPosition = new Vector2(rectTransform.rect.x + rectTransform.localPosition.x, rectTransform.rect.y + rectTransform.localPosition.y);
                }
                else
                {
                    m_State = AdjustWindowState.Right;
                    startWindowPosition = new Vector2(rectTransform.rect.x + rectTransform.localPosition.x, 0.0f);
                }
            }
            else if(localPosition.y >= max.y - topMoveSize) //Move
            {
                m_State = AdjustWindowState.Move;
                startWindowPosition = rectTransform.localPosition;
            }
        }

        void IDragHandler.OnDrag(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, null, out Vector2 localPosition);

            //Note: I'm sure theres a way to do the same thing without a switch statement. A minor optimization as things go.
            switch (m_State)
            {
                case AdjustWindowState.None:
                    return;
                case AdjustWindowState.Move:
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.pressPosition, null, out Vector2 localPressPosition);
                    rectTransform.localPosition = startWindowPosition - (localPressPosition - localPosition);
                    return;
                case AdjustWindowState.Left:

                    float targetSize = Mathf.Max(startWindowPosition.x - (localPosition.x + rectTransform.localPosition.x), m_MinSize.x);
                    float parentSize = 0.0f;
                    RectTransform parentRectTransform = rectTransform.parent as RectTransform;
                    if (rectTransform != null)
                    {
                        parentSize = parentRectTransform.rect.width;
                    }
                    rectTransform.sizeDelta = new Vector2(targetSize - parentSize * (rectTransform.anchorMax.x - rectTransform.anchorMin.x), rectTransform.sizeDelta.y);

                    Cursor.SetResizeLeftRightCursor();

                    rectTransform.localPosition = new Vector3(-(rectTransform.rect.position.x + rectTransform.rect.width) + startWindowPosition.x, rectTransform.localPosition.y, rectTransform.localPosition.z);
                    return;
                case AdjustWindowState.Right:
                    targetSize = Mathf.Max((localPosition.x + rectTransform.localPosition.x) -startWindowPosition.x, m_MinSize.x);
                    parentSize = 0.0f;
                    parentRectTransform = rectTransform.parent as RectTransform;
                    if (rectTransform != null)
                    {
                        parentSize = parentRectTransform.rect.width;
                    }
                    rectTransform.sizeDelta = new Vector2(targetSize - parentSize * (rectTransform.anchorMax.x - rectTransform.anchorMin.x), rectTransform.sizeDelta.y);

                    Cursor.SetResizeLeftRightCursor();

                    rectTransform.localPosition = new Vector3(-(rectTransform.rect.position.x) + startWindowPosition.x, rectTransform.localPosition.y, rectTransform.localPosition.z);
                    return;
                case AdjustWindowState.Top:
                    targetSize = Mathf.Max((localPosition.y + rectTransform.localPosition.y) - startWindowPosition.y, m_MinSize.y);
                    parentSize = 0.0f;
                    parentRectTransform = rectTransform.parent as RectTransform;
                    if (rectTransform != null)
                    {
                        parentSize = parentRectTransform.rect.height;
                    }
                    rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, targetSize - parentSize * (rectTransform.anchorMax.y - rectTransform.anchorMin.y));

                    Cursor.SetResizeUpDownCursor();

                    rectTransform.localPosition = new Vector3(rectTransform.localPosition.x, -(rectTransform.rect.position.y) + startWindowPosition.y, rectTransform.localPosition.z);

                    return;
                case AdjustWindowState.Bottom:
                    targetSize = Mathf.Max(startWindowPosition.y - (localPosition.y + rectTransform.localPosition.y), m_MinSize.y);
                    parentSize = 0.0f;
                    parentRectTransform = rectTransform.parent as RectTransform;
                    if (rectTransform != null)
                    {
                        parentSize = parentRectTransform.rect.height;
                    }
                    rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, targetSize - parentSize * (rectTransform.anchorMax.y - rectTransform.anchorMin.y));

                    Cursor.SetResizeUpDownCursor();

                    rectTransform.localPosition = new Vector3(rectTransform.localPosition.x, -(rectTransform.rect.position.y + rectTransform.rect.height) + startWindowPosition.y, rectTransform.localPosition.z);
                    return;
                case AdjustWindowState.TopLeft:
                    float targetWidth = Mathf.Max(startWindowPosition.x - (localPosition.x + rectTransform.localPosition.x), m_MinSize.x);
                    float targetHeight = Mathf.Max((localPosition.y + rectTransform.localPosition.y) - startWindowPosition.y, m_MinSize.y);
                    Vector2 parentSizes = Vector2.zero;
                    parentRectTransform = rectTransform.parent as RectTransform;
                    if (rectTransform != null)
                    {
                        parentSizes = parentRectTransform.rect.size;
                    }
                    rectTransform.sizeDelta = new Vector2(targetWidth - parentSizes.x * (rectTransform.anchorMax.x - rectTransform.anchorMin.x), targetHeight - parentSizes.y * (rectTransform.anchorMax.y - rectTransform.anchorMin.y));

                    Cursor.SetResizeTLBRCursor();

                    rectTransform.localPosition = new Vector3(-(rectTransform.rect.position.x + rectTransform.rect.width) + startWindowPosition.x, -(rectTransform.rect.position.y) + startWindowPosition.y, rectTransform.localPosition.z);
                    return;
                case AdjustWindowState.TopRight:
                    targetWidth = Mathf.Max((localPosition.x + rectTransform.localPosition.x) - startWindowPosition.x, m_MinSize.x);
                    targetHeight = Mathf.Max((localPosition.y + rectTransform.localPosition.y) - startWindowPosition.y, m_MinSize.y);
                    parentSizes = Vector2.zero;
                    parentRectTransform = rectTransform.parent as RectTransform;
                    if (rectTransform != null)
                    {
                        parentSizes = parentRectTransform.rect.size;
                    }
                    rectTransform.sizeDelta = new Vector2(targetWidth - parentSizes.x * (rectTransform.anchorMax.x - rectTransform.anchorMin.x), targetHeight - parentSizes.y * (rectTransform.anchorMax.y - rectTransform.anchorMin.y));

                    Cursor.SetResizeTRBLCursor();

                    rectTransform.localPosition = new Vector3(-(rectTransform.rect.position.x) + startWindowPosition.x, -(rectTransform.rect.position.y) + startWindowPosition.y, rectTransform.localPosition.z);
                    return;
                case AdjustWindowState.BottomLeft:
                    targetWidth = Mathf.Max(startWindowPosition.x - (localPosition.x + rectTransform.localPosition.x), m_MinSize.x);
                    targetHeight = Mathf.Max(startWindowPosition.y - (localPosition.y + rectTransform.localPosition.y), m_MinSize.y);
                    parentSizes = Vector2.zero;
                    parentRectTransform = rectTransform.parent as RectTransform;
                    if (rectTransform != null)
                    {
                        parentSizes = parentRectTransform.rect.size;
                    }
                    rectTransform.sizeDelta = new Vector2(targetWidth - parentSizes.x * (rectTransform.anchorMax.x - rectTransform.anchorMin.x), targetHeight - parentSizes.y * (rectTransform.anchorMax.y - rectTransform.anchorMin.y));

                    Cursor.SetResizeTRBLCursor();

                    rectTransform.localPosition = new Vector3(-(rectTransform.rect.position.x + rectTransform.rect.width) + startWindowPosition.x, -(rectTransform.rect.position.y + rectTransform.rect.height) + startWindowPosition.y, rectTransform.localPosition.z);
                    return;
                case AdjustWindowState.BottomRight:
                    targetWidth = Mathf.Max((localPosition.x + rectTransform.localPosition.x) - startWindowPosition.x, m_MinSize.x);
                    targetHeight = Mathf.Max(startWindowPosition.y - (localPosition.y + rectTransform.localPosition.y), m_MinSize.y);
                    parentSizes = Vector2.zero;
                    parentRectTransform = rectTransform.parent as RectTransform;
                    if (rectTransform != null)
                    {
                        parentSizes = parentRectTransform.rect.size;
                    }
                    rectTransform.sizeDelta = new Vector2(targetWidth - parentSizes.x * (rectTransform.anchorMax.x - rectTransform.anchorMin.x), targetHeight - parentSizes.y * (rectTransform.anchorMax.y - rectTransform.anchorMin.y));

                    Cursor.SetResizeTLBRCursor();

                    rectTransform.localPosition = new Vector3(-(rectTransform.rect.position.x) + startWindowPosition.x, -(rectTransform.rect.position.y + rectTransform.rect.height) + startWindowPosition.y, rectTransform.localPosition.z);
                    return;
            }
        }

        private Vector2 ParentLocalToLocal(Vector2 point)
        {
            return point - (Vector2)rectTransform.localPosition;
        }

        private Vector2 LocalToParentLocal(Vector2 point)
        {
            return point + (Vector2)rectTransform.localPosition;
        }

        private Vector2 RectToLocal(Vector2 point)
        {
            return point + rectTransform.rect.position;
        }

        private Vector2 LocalToRect(Vector2 point)
        {
            return point - rectTransform.rect.position;
        }

        private Vector2 ParentLocalToRect(Vector2 point)
        {
            return LocalToRect(ParentLocalToLocal(point));
        }

        void IEndDragHandler.OnEndDrag(PointerEventData eventData)
        {
            EndDrag();
        }

        private void EndDrag()
        {
            m_State = AdjustWindowState.None;

            //Make sure the window ends within screen bounds
            FixWindowViewAlignment();
        }

        void IPointerMoveHandler.OnPointerMove(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, null, out Vector2 localPosition);

            Vector2 min = new Vector2(rectTransform.rect.x + m_HalfAdjustEdgeSize, rectTransform.rect.y + m_HalfAdjustEdgeSize);
            Vector2 max = new Vector2(rectTransform.rect.x + rectTransform.rect.width - m_HalfAdjustEdgeSize, rectTransform.rect.y + rectTransform.rect.height - m_HalfAdjustEdgeSize);

            if (localPosition.y >= max.y) //Top Edge
            {
                if (localPosition.x <= min.x + extraCornerAdjustSize)
                {
                    Cursor.SetResizeTLBRCursor();
                }
                else if (localPosition.x >= max.x - extraCornerAdjustSize)
                {
                    Cursor.SetResizeTRBLCursor();
                }
                else
                {
                    Cursor.SetResizeUpDownCursor();
                }
            }
            else if (localPosition.y <= min.y) //Bottom Edge
            {
                if (localPosition.x <= min.x + extraCornerAdjustSize)
                {
                    Cursor.SetResizeTRBLCursor();
                }
                else if (localPosition.x >= max.x - extraCornerAdjustSize)
                {
                    Cursor.SetResizeTLBRCursor();
                }
                else
                {
                    Cursor.SetResizeUpDownCursor();
                }
            }
            else if (localPosition.x <= min.x) //Left Edge
            {
                if(localPosition.y <= min.y + extraCornerAdjustSize)
                {
                    Cursor.SetResizeTRBLCursor();
                }
                else if(localPosition.y >= max.y - extraCornerAdjustSize)
                {
                    Cursor.SetResizeTLBRCursor();
                }
                else
                {
                    Cursor.SetResizeLeftRightCursor();
                }
            }
            else if (localPosition.x >= max.x) //Right Edge
            {
                if (localPosition.y <= min.y + extraCornerAdjustSize)
                {
                    Cursor.SetResizeTLBRCursor();
                }
                else if (localPosition.y >= max.y - extraCornerAdjustSize)
                {
                    Cursor.SetResizeTRBLCursor();
                }
                else
                {
                    Cursor.SetResizeLeftRightCursor();
                }
            }
            else
            {
                Cursor.SetArrowCursor();
            }
        }

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            //We add this just in case a graphic was added to this component. This ensure we can set the raycast padding for better mouse window adjustment interaction.
            if(m_TargetGraphic == null)
            {
                m_TargetGraphic = GetComponent<Graphic>();
                if(m_TargetGraphic != null)
                {
                    m_TargetGraphic.raycastPadding = new Vector4(-m_HalfAdjustEdgeSize, -m_HalfAdjustEdgeSize, -m_HalfAdjustEdgeSize, -m_HalfAdjustEdgeSize);
                }
            }
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            Cursor.SetArrowCursor();
        }

        private void FixWindowViewAlignment()
        {
            if(m_DraggableRect == null) return;

            Vector2 rectWorldMin = rectTransform.TransformPoint(new Vector3(rectTransform.rect.x, rectTransform.rect.y, 0.0f));
            Vector2 rectWorldMax = rectTransform.TransformPoint(new Vector3(rectTransform.rect.x + rectTransform.rect.width, rectTransform.rect.y + rectTransform.rect.height, 0.0f));


            float worldEdgeSize = rectTransform.TransformPoint(new Vector3(rectTransform.rect.x + m_AdjustEdgeSize, 0.0f, 0.0f)).x - rectWorldMin.x;
            float worldTopEdgeSize = rectTransform.TransformPoint(new Vector3(rectTransform.rect.x + m_TopMoveSize, 0.0f, 0.0f)).x - rectWorldMin.x;

            Vector2 boundsWorldMin = m_DraggableRect.TransformPoint(new Vector3(m_DraggableRect.rect.x, m_DraggableRect.rect.y, 0.0f));
            Vector2 boundsWorldMax = m_DraggableRect.TransformPoint(new Vector3(m_DraggableRect.rect.x + m_DraggableRect.rect.width, m_DraggableRect.rect.y + m_DraggableRect.rect.height, 0.0f));

            boundsWorldMin += new Vector2(worldEdgeSize, worldEdgeSize + worldTopEdgeSize);
            boundsWorldMax += new Vector2(-worldEdgeSize, 0.0f);

            if (rectWorldMin.x >= boundsWorldMax.x)
            {
                float localX = rectTransform.InverseTransformPoint(boundsWorldMax.x, 0.0f, 0.0f).x;
                rectTransform.localPosition = new Vector3(rectTransform.localPosition.x - (rectTransform.rect.x - localX), rectTransform.localPosition.y, rectTransform.localPosition.z);
            }
            else if (rectWorldMax.x <= boundsWorldMin.x)
            {
                float localX = rectTransform.InverseTransformPoint(boundsWorldMin.x, 0.0f, 0.0f).x;
                rectTransform.localPosition = new Vector3(rectTransform.localPosition.x - (rectTransform.rect.x + rectTransform.rect.width - localX), rectTransform.localPosition.y, rectTransform.localPosition.z);
            }

            if (rectWorldMax.y >= boundsWorldMax.y)
            {
                float localY = rectTransform.InverseTransformPoint(0.0f, boundsWorldMax.y, 0.0f).y;
                rectTransform.localPosition = new Vector3(rectTransform.localPosition.x, rectTransform.localPosition.y - (rectTransform.rect.y + rectTransform.rect.height - localY), rectTransform.localPosition.z);
            }
            else if (rectWorldMax.y <= boundsWorldMin.y)
            {
                float localY = rectTransform.InverseTransformPoint(0.0f, boundsWorldMin.y, 0.0f).y;
                rectTransform.localPosition = new Vector3(rectTransform.localPosition.x,rectTransform.localPosition.y - (rectTransform.rect.y + rectTransform.rect.height - localY), rectTransform.localPosition.z);
            }
        }
    }
}
