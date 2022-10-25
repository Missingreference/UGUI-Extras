/// Notes about usage:
/// Using a TextMeshProUGUI object by itself causes major performance issues when trying to update the text frequently or simply appending a large amount of text to it. That is why TextArea was created for to solve that problem.
/// Any text outside the visible area is not rendered however it is recommended to use a RectMask2D to properly mask out the bottom line since the height of the last line usually overlaps the bottom of the bounds RectTransform area.
/// Otherwise you can make the height of this object's RectTransform divisible by the font size since the font size determines the height of each line.
/// Resizing the width of the RectTransform causes the text to be reparsed which can be a slow operation if there is a lot of text.
/// When parsing characters that do no exist in the font, they will be replaced with different characters in the actual TextMeshProUGUI lines.
/// A lot of features of TextMeshProUGUI may not work as expected or may have odd spacing issues due to optimizing of parsing text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Unity.Collections.LowLevel.Unsafe;
using Debug = UnityEngine.Debug;

using Elanetic.Native;
using Cursor = Elanetic.Native.Cursor;

namespace Elanetic.UI.Unity
{
    /// <summary>
    /// A performant way to render an area of text and be able to update the values quickly. Append, RemoveText and Clear are faster than SetText.
    /// This solution parses the text to determine how many characters fit in each line and allows for rendering each line as individual TextMeshProUGUI objects. Very useful for cases when presenting rapidly updating information such as console windows or chat boxes.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class TextArea : UIBehaviour, IScrollHandler, ICanvasElement, IPointerMoveHandler, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IDragHandler, IEndDragHandler, ISelectHandler, IUpdateSelectedHandler, IDeselectHandler
    {
        /// <summary>
        /// The target line index to view. The line index will begin at the top of the text area and render the remaining lines from that index. The target line can never be scrolled past the value of lastLineIndex.
        /// </summary>
        public int targetLineIndex
        {
            get => m_TargetLineIndex;
            set
            {
                m_TargetLineIndex = Mathf.Clamp(value, 0, m_LastLineIndex);
                m_LastPossibleVisibleIndex = m_TargetLineIndex + m_MaxVisibleLineCount - 1;

                if (isActiveAndEnabled)
                    Refresh();
                else
                    m_IsDirty = true;

                onTargetLineChanged?.Invoke();
            }
        }

        /// <summary>
        /// The last index that can be scrolled to. If the number of lines is less than the value of maxVisibleLineCount then the value will be 0 meaning that the Text Area cannot be scrolled.
        /// </summary>
        public int lastLineIndex => m_LastLineIndex;

        /// <summary>
        /// The number of lines of text.
        /// </summary>
        public int lineCount => m_LineCount;

        /// <summary>
        /// The maximum possible lines that can fit within the height of the Text Area. This is determined by the RectTransform.rect.height and font size.
        /// </summary>
        public int maxVisibleLineCount => m_MaxVisibleLineCount;

        /// <summary>
        /// A reference to the RectTransform.
        /// </summary>
        public RectTransform rectTransform => (RectTransform)transform;

        /// <summary>
        /// The size of the font. Changing this will require a reparsing of all text, a slow operation.
        /// </summary>
        public float fontSize
        {
            get => m_FontSize;
            set
            {
                m_FontSize = value;

                for (int i = 0; i < m_VisibleLineCount; i++)
                {
                    TextMeshProUGUI textMesh = m_VisibleLines[i];
                    textMesh.fontSize = value;
                    textMesh.rectTransform.offsetMin = new Vector2(1.0f, 0.0f);
                    textMesh.rectTransform.offsetMax = new Vector2(-1.0f, fontSize);
                }

                //Setup font values
                m_FontPointSizeScale = fontSize / (font.faceInfo.pointSize * font.faceInfo.scale);
                m_FontEmScale = fontSize * 0.01f;
                FontStyles style = FontStyles.Normal;
                float styleSpacingAdjustment = (style & FontStyles.Bold) == FontStyles.Bold ? font.boldSpacing : 0;
                float normalSpacingAdjustment = font.normalSpacingOffset;
                m_FontSpacingAdjustment = styleSpacingAdjustment + normalSpacingAdjustment;
                m_FontTabSize = font.faceInfo.tabWidth * font.tabSize * m_FontPointSizeScale + m_FontSpacingAdjustment * m_FontEmScale;

                //Force rect size update. As a result OnRectTransformDimensionsChange will call RecalculateLines where it will reparse all the text. Aftwards it will then call Refresh.
                m_LastRectSize = new Vector2(float.MinValue, float.MinValue);
                OnRectTransformDimensionsChange();
            }
        }

        /// <summary>
        /// The font to render the text of the Text Area. Changing this will require reparsing all text, a slow operation if there is a lot of text.
        /// </summary>
        public TMP_FontAsset font
        {
            get => m_Font;
            set
            {
                m_Font = value;

                if(m_Font == null)
                {
                    throw new NullReferenceException("The inputted font is null. A Text Area must use a valid font asset.");
                }

                for(int i = 0; i < m_VisibleLineCount; i++)
                {
                    m_VisibleLines[i].font = value;
                }

                //Setup font values
                m_FontPointSizeScale = fontSize / (font.faceInfo.pointSize * font.faceInfo.scale);
                m_FontEmScale = fontSize * 0.01f;
                FontStyles style = FontStyles.Normal;
                float styleSpacingAdjustment = (style & FontStyles.Bold) == FontStyles.Bold ? font.boldSpacing : 0;
                float normalSpacingAdjustment = font.normalSpacingOffset;
                m_FontSpacingAdjustment = styleSpacingAdjustment + normalSpacingAdjustment;
                m_FontTabSize = font.faceInfo.tabWidth * font.tabSize * m_FontPointSizeScale + m_FontSpacingAdjustment * m_FontEmScale;

                //Pick missing character replacement
                if (font.characterLookupTable.TryGetValue(SQUARE_CHAR, out m_TMPMissingCharacterReplacement))
                {
                    //Replace char with Square character(/u25A1)
                    m_MissingCharacterReplacement = '□';
                }
                else if (font.characterLookupTable.TryGetValue(REPLACEMENT_CHAR, out m_TMPMissingCharacterReplacement))
                {
                    //Replace char with Replacement character(/uFFFD)
                    m_MissingCharacterReplacement = '�';
                }
                else
                {
                    //Replace char with a space
                    m_MissingCharacterReplacement = ' ';
                }

                //Force rect size update
                m_LastRectSize = new Vector2(float.MinValue, float.MinValue);
                OnRectTransformDimensionsChange();
            }
        }

        /// <summary>
        /// If scroll percent is already 100%(scrolled all the way to the bottom even when the entire view has not been filled) whenever new lines are added make sure the view is already scrolled to the bottom.
        /// </summary>
        public bool autoScrollToBottom { get; set; } = false;

        /// <summary>
        /// The scroll bar to control this text area's vertical line position.
        /// </summary>
        public Scrollbar scrollBar
        {
            get => m_ScrollBar;
            set
            {
                m_ScrollBar?.onValueChanged.RemoveListener(OnScrollBarValueChanged);

                m_ScrollBar = value;
                if(m_ScrollBar != null)
                {
                    m_ScrollBar.onValueChanged.AddListener(OnScrollBarValueChanged);

                    float percent = 1.0f;
                    if (m_LastLineIndex > 0)
                        percent = (float)m_TargetLineIndex / (float)m_LastLineIndex;
                    m_ScrollBar.SetValueWithoutNotify(percent);
                }
            }
        }

        /// <summary>
        /// Enable whether user input can interact with this text area to highlight and copy text.
        /// Functions like SelectText and DeselectText still work regardless of this value.
        /// </summary>
        public bool highlightable
        {
            get => m_Highlightable;
            set
            {
                m_Highlightable = value;
                if(!m_Highlightable && m_IsDragging)
                {
                    EndDrag();
                    Cursor.SetArrowCursor();
                }
            }
        }

        /// <summary>
        /// The color of the background of highlighted text.
        /// </summary>
        public Color highlightBackgroundColor
        {
            get => m_HighlightBackgroundColor;
            set
            {
                m_HighlightBackgroundColor = value;
                CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild(this);
            }
        }

        /// <summary>
        /// The scrolling speed when dragging to highlight text and the cursor reaches out of bounds above or below the visible text area.
        /// For example, setting a drag scroll speed of 1.0f when the mouse is dragging exactly one line above the text area, the text area will scroll up every one second of unscaled time.
        /// </summary>
        public float dragScrollSpeed
        {
            get => m_DragScrollSpeed;
            set
            {
                m_DragScrollSpeed = value;
            }
        }

        //Events
        public Action onTargetLineChanged;


        //Settings
        private float m_FontSize = 16.0f;
        private TMP_FontAsset m_Font;
        private Scrollbar m_ScrollBar;
        private int m_TargetLineIndex = 0;
        private int m_LastPossibleVisibleIndex = 0;

        //Char data
        private char[] m_RawText = new char[64];
        private int m_RawLength = 0;
        private Color32[] m_CharacterColors = new Color32[64];
        private List<int> m_ReplacedCharacterIndexs = new List<int>();
        private List<char> m_OriginalReplacedCharacters = new List<char>();

        //Line info
        private List<int> m_LineStartIndexs = new List<int>(16);
        private List<int> m_LineSizes = new List<int>(16);

        //Visible info
        private TextMeshProUGUI[] m_VisibleLines = new TextMeshProUGUI[8];
        private int m_VisibleLineCount = 0;

        //State
        private int m_MaxVisibleLineCount = 0;
        private int m_LineCount = 0;
        private int m_LastLineIndex = 0;
        private bool m_IsDirty = false;
        private Vector2 m_LastRectSize = new Vector2(float.MinValue, float.MinValue);

        //Pool
        private List<TextMeshProUGUI> m_TextMeshPool = new List<TextMeshProUGUI>(16);
        private Transform m_PoolParent;

        //Highlighting
        private bool m_Highlightable = true;
        private CanvasRenderer m_HighLightRenderer;
        private Mesh m_Mesh;
        private bool m_PointerIsDown = false;
        private bool m_IsDragging = false;
        private float m_DragStartPositionX;
        private int m_DragStartLine;
        private float m_DragScrollSpeed = 2.0f;
        private int m_StartSelectedIndex = -1;
        private int m_EndSelectedIndex = -1;
        private Color m_HighlightTextColor = Color.white;
        private Color m_HighlightBackgroundColor = new Color(0.2f, 0.8f, 1.0f, 1.0f);
        private float m_DoubleClickTime = 0.5f; //500ms default Windows double click time
        private float m_DoubleClickTimer;

        //Font setup
        private float m_FontPointSizeScale;
        private float m_FontEmScale;
        private float m_FontSpacingAdjustment;
        private float m_FontTabSize;

        //Missing character replacements
        private char m_MissingCharacterReplacement;
        private TMP_Character m_TMPMissingCharacterReplacement;
        private const uint SQUARE_CHAR = 9633;
        private const uint REPLACEMENT_CHAR = 65533;

        protected override void Awake()
        {
            font = TMP_Settings.defaultFontAsset;

            //Create pool
            m_PoolParent = new GameObject("Text Line Pool").transform;
            m_PoolParent.SetParent(transform);
            m_PoolParent.localPosition = Vector3.zero;
            m_PoolParent.localRotation = Quaternion.identity;
            m_PoolParent.localScale = Vector3.one;
            m_PoolParent.gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        protected override void OnEnable()
        {
            if(m_IsDirty)
            {
                Refresh();
                m_IsDirty = false;
            }
            else
            {
                //Reupload vertex colors
                UploadTextColorData();
            }


            GameObject highlightObject = new GameObject("Highlight", typeof(TMP_SelectionCaret));

            highlightObject.hideFlags = HideFlags.DontSave;
            highlightObject.transform.SetParent(transform);
            highlightObject.transform.SetAsFirstSibling();
            highlightObject.layer = gameObject.layer;

            RectTransform highlightRectTransform = highlightObject.GetComponent<RectTransform>();
            highlightRectTransform.anchorMin = Vector2.zero;
            highlightRectTransform.anchorMax = Vector2.one;
            highlightRectTransform.pivot = new Vector2(0.5f, 0.5f);
            highlightRectTransform.localScale = Vector3.one;
            highlightRectTransform.localRotation = Quaternion.identity;
            highlightRectTransform.offsetMin = Vector2.zero;
            highlightRectTransform.offsetMax = Vector2.zero;
            m_HighLightRenderer = highlightObject.GetComponent<CanvasRenderer>();
            m_HighLightRenderer.SetMaterial(Graphic.defaultGraphicMaterial, Texture2D.whiteTexture);
        }

        protected override void OnDisable()
        {
            if(m_HighLightRenderer != null)
            {
                Destroy(m_HighLightRenderer.gameObject);
            }
            if(m_Mesh != null)
            {
                DestroyImmediate(m_Mesh);
            }

            EndDrag();
            DeselectText();
        }

        protected override void OnDestroy()
        {
            for (int i = 0; i < m_VisibleLineCount; i++)
            {
                Destroy(m_VisibleLines[i].gameObject);
            }
            Destroy(m_PoolParent.gameObject);
        }

        private void ParseText()
        {
            int index = 0;
            int lineStartIndex = 0;
            float currentLineWidth = 0;
            int lineWhitespaceIndex = -1;
            float currentWordWidth = 0;

            if(m_LineCount > 0)
            {
                //Deparse last line in case the last line is not full
                int lastIndex = m_LineCount - 1;
                index = m_LineStartIndexs[lastIndex];
                lineStartIndex = index;
                m_LineStartIndexs.RemoveAt(lastIndex);
                m_LineSizes.RemoveAt(lastIndex);
                m_LineCount--;

                //Put back the replaced characters of the last line in preperation for reparsing
                for (int i = m_ReplacedCharacterIndexs.Count-1; i >= 0; i--)
                {
                    int replacedCharacterIndex = m_ReplacedCharacterIndexs[i];
                    if (replacedCharacterIndex >= lineStartIndex)
                    {
                        m_RawText[replacedCharacterIndex] = m_OriginalReplacedCharacters[i];
                        m_ReplacedCharacterIndexs.RemoveAt(i);
                        m_OriginalReplacedCharacters.RemoveAt(i);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            float containerWidth = Mathf.Max(0.01f, rectTransform.rect.width-2.0f);
            void CreateLine()
            {
                m_LineStartIndexs.Add(lineStartIndex);
                m_LineSizes.Add(index - lineStartIndex);

                m_LineCount++;
            }

            for(; index < m_RawLength; index++)
            {
                char unicode = m_RawText[index];
                float charWidth;
                //Handle special characters
                if(unicode == 9) //9 is the Tab character (\t)
                {
                    float tabs = Mathf.Ceil(currentLineWidth / m_FontTabSize) * m_FontTabSize;
                    if(tabs > currentLineWidth)
                    {
                        charWidth = tabs - currentLineWidth;
                        currentLineWidth = tabs;
                    }
                    else
                    {
                        charWidth = m_FontTabSize;
                        currentLineWidth += m_FontTabSize;
                    }

                    //If tabs is the cause of a new line, that means the tab size will be different when wrapped to a new line
                    if(currentLineWidth >= containerWidth && (index - lineStartIndex) > 0)
                    {
                        CreateLine();
                        lineStartIndex = index;

                        tabs = Mathf.Ceil(0.0f / m_FontTabSize) * m_FontTabSize;
                        if (tabs > currentLineWidth)
                        {
                            charWidth = tabs - currentLineWidth;
                            currentLineWidth = tabs;
                        }
                        else
                        {
                            charWidth = m_FontTabSize;
                            currentLineWidth += m_FontTabSize;
                        }
                    }

                    lineWhitespaceIndex = -1;
                    currentWordWidth = 0.0f;
                }
                else if(unicode == '\n') //Parse new line (\n or \r\n)
                {
                    CreateLine();
                    lineStartIndex = index + 1;
                    lineWhitespaceIndex = -1;
                    currentWordWidth = 0.0f;
                    currentLineWidth = 0.0f;
                    continue;
                }
                else if(unicode == '\r' && (index + 1) < m_RawLength && m_RawText[index + 1] == '\n')
                {
                    CreateLine();
                    lineStartIndex = index + 2; 
                    index += 1;
                    lineWhitespaceIndex = -1;
                    currentWordWidth = 0.0f;
                    currentLineWidth = 0.0f;
                    continue;
                }
                else
                {
                    // Make sure the given unicode exists in the font asset.
                    if (!font.characterLookupTable.TryGetValue(unicode, out TMP_Character character))
                    {
                        //Target character does not have a representation in the font. Replace with another filler error character.
                        //This replaces the character in the internal array for performance reasons. In the event the font changes you will need to recall SetText to parse the text again to show the correct text.
                        m_RawText[index] = m_MissingCharacterReplacement;
                        character = m_TMPMissingCharacterReplacement;

                        m_ReplacedCharacterIndexs.Add(index);
                        m_OriginalReplacedCharacters.Add(unicode);
                    }

                    charWidth = character.glyph.metrics.horizontalAdvance * m_FontPointSizeScale + m_FontSpacingAdjustment * m_FontEmScale;
                    currentLineWidth += charWidth;
                }

                if (currentLineWidth >= containerWidth && (index - lineStartIndex) > 0)
                {
                    if (lineWhitespaceIndex >= 0 && !char.IsWhiteSpace(unicode))
                    {
                        //Word wrap
                        int savedIndex = index;
                        index = lineWhitespaceIndex + 1;

                        CreateLine();

                        lineStartIndex = index;
                        index = savedIndex;
                        currentLineWidth = currentWordWidth + charWidth;
                    }
                    else
                    {
                        CreateLine();
                        lineStartIndex = index;
                        currentLineWidth = charWidth;
                    }

                    lineWhitespaceIndex = -1;
                    currentWordWidth = 0.0f;
                }
                else if (char.IsWhiteSpace(unicode))
                {
                    lineWhitespaceIndex = index;
                    currentWordWidth = 0;
                }
                else
                {
                    currentWordWidth += charWidth;
                }
            }

            //Last line
            if(lineStartIndex < m_RawLength)
            {
                CreateLine();
            }
        }

        public void SetText(string text) => SetText(text, Color.white);

        public void SetText(string text, Color color)
        {
            if(string.IsNullOrEmpty(text))
            {
                Clear();
                return;
            }

            m_LineCount = 0;
            m_LineStartIndexs.Clear();
            m_LineSizes.Clear();
            m_ReplacedCharacterIndexs.Clear();
            m_OriginalReplacedCharacters.Clear();

            //The new text should not be selected
            DeselectText();

            m_RawLength = text.Length;
            if(m_RawLength > m_RawText.Length)
            {
                //Resize raw char array
                int newSize = ((m_RawLength / 64) + 1) * 64;
                m_RawText = new char[newSize];
                m_CharacterColors = new Color32[newSize];
            }

            //Copy text memory to raw char array
            unsafe
            {
                fixed(char* srcPtr = text)
                fixed(char* destPtr = &m_RawText[0])
                    UnsafeUtility.MemCpy(destPtr, srcPtr, m_RawLength * 2);

                //Copy color over memory spread
                Color32 c = (Color32)color;
                Color32* clrPtr = (Color32*)&c;
                fixed(Color32* colorDestPtr = &m_CharacterColors[0])
                {
                    UnsafeUtility.MemCpyReplicate(colorDestPtr, clrPtr, 4, m_RawLength);
                }
            }

            ParseText();

            if (m_LineCount > m_VisibleLines.Length)
            {
                //Resize visible line array
                int newSize = m_VisibleLines.Length;
                while (newSize < m_LineCount) newSize *= 2;
                Array.Resize(ref m_VisibleLines, newSize);
            }

            m_LastLineIndex = Mathf.Clamp(m_LineCount - m_MaxVisibleLineCount + 1, 0, m_LineCount - 1);

            if (autoScrollToBottom)
            {
                m_TargetLineIndex = m_LastLineIndex;
            }
            else
            {
                m_TargetLineIndex = Mathf.Clamp(m_TargetLineIndex, 0, m_LastLineIndex);
            }
            m_LastPossibleVisibleIndex = m_TargetLineIndex + m_MaxVisibleLineCount - 1;

            if(isActiveAndEnabled)
                Refresh();
            else
                m_IsDirty = true;

            onTargetLineChanged?.Invoke();
        }

        public void Append(string text) => Append(text, Color.white);

        public void Append(string text, Color color)
        {
            if(text.Length == 0) return;

            int sourceLength = text.Length;
            int newLength = sourceLength + m_RawLength;
            if(newLength > m_RawText.Length)
            {
                //Resize raw char array
                int newSize = ((newLength / 64) + 1) * 64;
                char[] newCharArray = new char[newSize];
                Color32[] newColorArray = new Color32[newSize];

                unsafe
                {
                    fixed(char* destPtr = &newCharArray[0])
                    {
                        //Copy old char array to new char array
                        fixed(char* srcPtr = &m_RawText[0])
                            UnsafeUtility.MemCpy(destPtr, srcPtr, m_RawLength * 2);


                        //Copy new text to after the old text of the char array
                        char* appendPtr = destPtr + m_RawLength;
                        fixed(char* srcPtr = text)
                            UnsafeUtility.MemCpy(appendPtr, srcPtr, sourceLength * 2);
                    }


                    fixed(Color32* clrDestPtr = &newColorArray[0])
                    {
                        fixed(Color32* clrSrcPtr = &m_CharacterColors[0])
                            UnsafeUtility.MemCpy(clrDestPtr, clrSrcPtr, m_RawLength * 4);

                        Color32 c = (Color32)color;
                        Color32* clrPtr = (Color32*)&c;
                        Color32* clrAppendPtr = clrDestPtr + m_RawLength;
                        UnsafeUtility.MemCpyReplicate(clrAppendPtr, clrPtr, 4, sourceLength);

                    }
                }

                m_RawText = newCharArray;
                m_CharacterColors = newColorArray;
            }
            else
            {
                unsafe
                {
                    //Copy text memory to raw char array
                    fixed(char* srcPtr = text)
                    fixed(char* destPtr = &m_RawText[m_RawLength])
                        UnsafeUtility.MemCpy(destPtr, srcPtr, sourceLength * 2);

                    //Copy color over memory spread
                    Color32 c = (Color32)color;
                    Color32* clrPtr = (Color32*)&c;
                    fixed(Color32* colorDestPtr = &m_CharacterColors[m_RawLength])
                    {
                        UnsafeUtility.MemCpyReplicate(colorDestPtr, clrPtr, 4, sourceLength);
                    }
                }
            }

            m_RawLength = newLength;
            ParseText();

            if (m_LineCount > m_VisibleLines.Length)
            {
                //Resize visible line array
                int newSize = m_VisibleLines.Length;
                while (newSize < m_LineCount) newSize *= 2;
                Array.Resize(ref m_VisibleLines, newSize);
            }

            m_LastLineIndex = Mathf.Clamp(m_LineCount - m_MaxVisibleLineCount + 1, 0, m_LineCount - 1);

            if (autoScrollToBottom)
            {
                m_TargetLineIndex = m_LastLineIndex;
                m_LastPossibleVisibleIndex = m_TargetLineIndex + m_MaxVisibleLineCount - 1;
            }

            if (isActiveAndEnabled)
                Refresh();
            else
                m_IsDirty = true;

            onTargetLineChanged?.Invoke();
        }

        public void Append(char[] characterData, Color32[] colorData, int startIndex, int count)
        {
            if(startIndex < 0 || startIndex >= characterData.Length || count <= 0)
            {
                return;
            }

#if DEBUG
            if(startIndex + count > characterData.Length || startIndex + count > colorData.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Start index + count surpases the length of the inputted character data or color data");
            }
#endif

            int sourceLength = count;
            int newLength = sourceLength + m_RawLength;
            if (newLength > m_RawText.Length)
            {
                //Resize raw char array
                int newSize = ((newLength / 64) + 1) * 64;
                char[] newCharArray = new char[newSize];
                Color32[] newColorArray = new Color32[newSize];

                unsafe
                {
                    fixed (char* destPtr = &newCharArray[0])
                    {
                        //Copy old char array to new char array
                        fixed (char* srcPtr = &m_RawText[0])
                            UnsafeUtility.MemCpy(destPtr, srcPtr, m_RawLength * 2);


                        //Copy new text to after the old text of the char array
                        char* appendPtr = destPtr + m_RawLength;
                        fixed (char* srcPtr = &characterData[startIndex])
                            UnsafeUtility.MemCpy(appendPtr, srcPtr, sourceLength * 2);
                    }


                    fixed (Color32* clrDestPtr = &newColorArray[0])
                    {
                        //Copy old color array to new color array
                        fixed (Color32* clrSrcPtr = &m_CharacterColors[0])
                            UnsafeUtility.MemCpy(clrDestPtr, clrSrcPtr, m_RawLength * 4);

                        Color32* clrAppendPtr = clrDestPtr + m_RawLength;
                        fixed(Color32* srcPtr = &colorData[startIndex])
                            UnsafeUtility.MemCpy(clrAppendPtr, srcPtr, sourceLength * 4);

                    }
                }

                m_RawText = newCharArray;
                m_CharacterColors = newColorArray;
            }
            else
            {
                unsafe
                {
                    //Copy text memory to raw char array
                    fixed (char* srcPtr = &characterData[startIndex])
                    fixed (char* destPtr = &m_RawText[m_RawLength])
                        UnsafeUtility.MemCpy(destPtr, srcPtr, sourceLength * 2);

                    //Copy color over memory spread
                    fixed(Color32* colorSrcPtr = &colorData[startIndex])
                    fixed (Color32* colorDestPtr = &m_CharacterColors[m_RawLength])
                    {
                        UnsafeUtility.MemCpy(colorDestPtr, colorSrcPtr, sourceLength * 4);
                    }
                }
            }

            m_RawLength = newLength;
            ParseText();

            if (m_LineCount > m_VisibleLines.Length)
            {
                //Resize visible line array
                int newSize = m_VisibleLines.Length;
                while (newSize < m_LineCount) newSize *= 2;
                Array.Resize(ref m_VisibleLines, newSize);
            }

            m_LastLineIndex = Mathf.Clamp(m_LineCount - m_MaxVisibleLineCount + 1, 0, m_LineCount - 1);

            if (autoScrollToBottom)
            {
                m_TargetLineIndex = m_LastLineIndex;
                m_LastPossibleVisibleIndex = m_TargetLineIndex + m_MaxVisibleLineCount - 1;
            }

            if (isActiveAndEnabled)
                Refresh();
            else
                m_IsDirty = true;

            onTargetLineChanged?.Invoke();
        }

        public void Append(char[] characterData, Color32 color, int startIndex, int count)
        {
            if(startIndex < 0 || startIndex >= characterData.Length || count < 0)
            {
                return;
            }

#if DEBUG
            if(startIndex + count > characterData.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Start index + count surpases the lenght of the inputted character data or color data");
            }
#endif

            int sourceLength = count;
            int newLength = sourceLength + m_RawLength;
            if(newLength > m_RawText.Length)
            {
                //Resize raw char array
                int newSize = ((newLength / 64) + 1) * 64;
                char[] newCharArray = new char[newSize];
                Color32[] newColorArray = new Color32[newSize];

                unsafe
                {
                    fixed(char* destPtr = &newCharArray[0])
                    {
                        //Copy old char array to new char array
                        fixed(char* srcPtr = &m_RawText[0])
                            UnsafeUtility.MemCpy(destPtr, srcPtr, m_RawLength * 2);


                        //Copy new text to after the old text of the char array
                        char* appendPtr = destPtr + m_RawLength;
                        fixed(char* srcPtr = &characterData[startIndex])
                            UnsafeUtility.MemCpy(appendPtr, srcPtr, sourceLength * 2);
                    }


                    fixed(Color32* clrDestPtr = &newColorArray[0])
                    {
                        //Copy old color array to new color array
                        fixed(Color32* clrSrcPtr = &m_CharacterColors[0])
                            UnsafeUtility.MemCpy(clrDestPtr, clrSrcPtr, m_RawLength * 4);

                        Color32* clrAppendPtr = clrDestPtr + m_RawLength;
                        UnsafeUtility.MemCpyReplicate(clrAppendPtr, &color, 4, sourceLength);
                    }
                }

                m_RawText = newCharArray;
                m_CharacterColors = newColorArray;
            }
            else
            {
                unsafe
                {
                    //Copy text memory to raw char array
                    fixed(char* srcPtr = &characterData[startIndex])
                    fixed(char* destPtr = &m_RawText[m_RawLength])
                        UnsafeUtility.MemCpy(destPtr, srcPtr, sourceLength * 2);

                    //Copy color over memory spread
                    fixed(Color32* colorDestPtr = &m_CharacterColors[m_RawLength])
                    {
                        UnsafeUtility.MemCpyReplicate(colorDestPtr, &color, 4, sourceLength);
                    }
                }
            }

            m_RawLength = newLength;
            ParseText();

            if(m_LineCount > m_VisibleLines.Length)
            {
                //Resize visible line array
                int newSize = m_VisibleLines.Length;
                while(newSize < m_LineCount) newSize *= 2;
                Array.Resize(ref m_VisibleLines, newSize);
            }

            m_LastLineIndex = Mathf.Clamp(m_LineCount - m_MaxVisibleLineCount + 1, 0, m_LineCount - 1);

            if(autoScrollToBottom)
            {
                m_TargetLineIndex = m_LastLineIndex;
                m_LastPossibleVisibleIndex = m_TargetLineIndex + m_MaxVisibleLineCount - 1;
            }

            if(isActiveAndEnabled)
                Refresh();
            else
                m_IsDirty = true;

            onTargetLineChanged?.Invoke();
        }

        public void RemoveText(int startIndex, int count)
        {
            if(startIndex < 0 || startIndex >= m_RawLength)
            {
                throw new IndexOutOfRangeException("Start Index must be more than or equal to 0 and less than the length of the text. Inputted: " + startIndex.ToString());
            }
            if(count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be more than zero.");
            }

            int endIndex = startIndex + count - 1;

            if(endIndex >= m_RawLength)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Inputted range to remove is out of bounds. End index: " + endIndex + " | Text length: " + m_RawLength);
            }

            //Handle selection
            if(m_StartSelectedIndex >= 0 && m_EndSelectedIndex >= 0)
            {
                if (m_StartSelectedIndex >= startIndex)
                {
                    if (m_EndSelectedIndex <= endIndex)
                    {
                        DeselectText();
                    }
                    else if (m_EndSelectedIndex > endIndex)
                    {
                        m_StartSelectedIndex = endIndex + 1 - count;
                        m_EndSelectedIndex -= count;
                    }
                }
                else if (m_EndSelectedIndex <= endIndex)
                {
                    m_EndSelectedIndex = startIndex - 1;
                }
                else
                {
                    m_EndSelectedIndex -= count;
                }

                CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild(this);
            }

            if(startIndex + count < m_RawLength)
            {
                //Move remaining characters
                unsafe
                {
                    int remaining = m_RawLength - endIndex - 1;
                    //Copy text memory to raw char array
                    fixed (char* destPtr = &m_RawText[startIndex])
                    {
                        char* srcPtr = destPtr + count;
                        UnsafeUtility.MemMove(destPtr, srcPtr, remaining * 2);
                    }

                    fixed (Color32* colorDestPtr = &m_CharacterColors[startIndex])
                    {
                        Color32* srcPtr = colorDestPtr + count;
                        UnsafeUtility.MemMove(colorDestPtr, srcPtr, remaining * 4);
                    }
                }

                //Adjust replaced characters
                int replacedCount = m_ReplacedCharacterIndexs.Count;
                int removeCount = -1;
                for (int i = 0; i < replacedCount; i++)
                {
                    if (m_ReplacedCharacterIndexs[i] < startIndex) continue;

                    for (int h = i; h < replacedCount; h++)
                    {
                        int replacedCharIndex = m_ReplacedCharacterIndexs[h];
                        if (replacedCharIndex > endIndex)
                        {
                            removeCount = h - i;
                            if (removeCount == 0) break;
                            m_ReplacedCharacterIndexs.RemoveRange(i,removeCount);
                            m_OriginalReplacedCharacters.RemoveRange(i, removeCount);
                            replacedCount -= removeCount;
                            for (int j = h; j < replacedCount; j++)
                            {
                                m_ReplacedCharacterIndexs[j] = replacedCharIndex - count;
                            }
                            break;
                        }
                    }

                    if(removeCount == -1)
                    {
                        m_ReplacedCharacterIndexs.RemoveRange(i, replacedCount - i);
                        m_OriginalReplacedCharacters.RemoveRange(i, replacedCount - i);
                    }

                    break;
                }
            }

            int oldLineCount = m_LineCount;

            //Reparse remaining lines
            for (int i = 0; i < m_LineCount; i++)
            {
                if (m_LineStartIndexs[i] + m_LineSizes[i] > startIndex)
                {
                    int linesToRemove = m_LineCount - i;
                    m_LineStartIndexs.RemoveRange(i, linesToRemove);
                    m_LineSizes.RemoveRange(i, linesToRemove);
                    m_LineCount -= linesToRemove;
                    break;
                }
            }


            m_RawLength -= count;

            ParseText();

            if (endIndex < m_TargetLineIndex)
            {
                m_TargetLineIndex -= oldLineCount - m_LineCount;
            }
            else
            {
                m_TargetLineIndex = Mathf.Clamp(m_TargetLineIndex - (oldLineCount - m_LineCount), 0, m_LastLineIndex);
            }

            m_LastPossibleVisibleIndex = m_TargetLineIndex + m_MaxVisibleLineCount - 1;
            m_LastLineIndex = Mathf.Clamp(m_LineCount - m_MaxVisibleLineCount + 1, 0, m_LineCount - 1);

            if (isActiveAndEnabled)
                Refresh();
            else
                m_IsDirty = true;

            onTargetLineChanged?.Invoke();
        }
        
        public void Clear()
        {
            EndDrag();
            DeselectText();

            m_TargetLineIndex = 0;
            m_LastPossibleVisibleIndex = m_TargetLineIndex + m_MaxVisibleLineCount - 1;

            m_LineSizes.Clear();
            m_LineStartIndexs.Clear();
            m_ReplacedCharacterIndexs.Clear();
            m_OriginalReplacedCharacters.Clear();

            m_LineCount = 0;
            m_RawLength = 0;
            m_LastLineIndex = 0;

            if(isActiveAndEnabled)
                Refresh();
            else
                m_IsDirty = true;

            onTargetLineChanged?.Invoke();
        }

        public void Refresh()
        {
            int linesLeft;
            if(m_LastPossibleVisibleIndex + 1 < m_LineCount)
            {
                linesLeft = m_MaxVisibleLineCount;
            }
            else
            {
                linesLeft = m_LineCount - m_TargetLineIndex;
            }
            int reuseCount = m_VisibleLineCount < linesLeft ? m_VisibleLineCount : linesLeft;

            //Reuse visible lines if they exist
            for (int i = 0; i < reuseCount; i++)
            {
                int targetIndex = m_TargetLineIndex + i;
                int lineStartIndex = m_LineStartIndexs[targetIndex];
                int lineSize = m_LineSizes[targetIndex];

                TextMeshProUGUI textMesh = m_VisibleLines[i];
                textMesh.SetCharArray(m_RawText, lineStartIndex, lineSize);
                textMesh.rectTransform.anchoredPosition = new Vector2(textMesh.rectTransform.anchoredPosition.x, -fontSize * i);

                if (lineSize <= 0) continue; //Without this check calling textMesh.UpdateVertexData pushes old data to the shader

                //Apply colors
                textMesh.ForceMeshUpdate();

                TMP_TextInfo textInfo = textMesh.textInfo;
                TMP_MeshInfo[] meshInfo = textInfo.meshInfo;
                for (int h = 0; h < lineSize; h++)
                {
                    TMP_CharacterInfo charInfo = textInfo.characterInfo[h];
                    //Skip past characters that are not visible. They do not have shader indexs and will cause issues if we try to apply colors to their vertexs
                    if (!charInfo.isVisible) continue;

                    int rawIndex = lineStartIndex + h;
                    int materialIndex = charInfo.materialReferenceIndex;
                    Color32[] colors = meshInfo[materialIndex].colors32;
                    Color32 targetColor = m_CharacterColors[rawIndex];

                    //Filter highlighted text for maximum visiblity
                    if (rawIndex >= m_StartSelectedIndex && rawIndex <= m_EndSelectedIndex)
                    {
                        targetColor = FilterHighlightedText(targetColor);
                    }

                    int vertexIndex = charInfo.vertexIndex;
                    colors[vertexIndex + 0] = targetColor;
                    colors[vertexIndex + 1] = targetColor;
                    colors[vertexIndex + 2] = targetColor;
                    colors[vertexIndex + 3] = targetColor;
                }

                textMesh.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            }

            if(m_VisibleLineCount < linesLeft)
            {
                //Pop new text meshs to fill the rest of the lines
                for (int i = m_VisibleLineCount; i < linesLeft; i++)
                {
                    int targetIndex = m_TargetLineIndex + i;

                    int lineStartIndex = m_LineStartIndexs[targetIndex];
                    int lineSize = m_LineSizes[targetIndex];

                    TextMeshProUGUI textMesh = PopTextMesh();
                    m_VisibleLines[i] = textMesh;
                    m_VisibleLineCount++;

                    textMesh.SetCharArray(m_RawText, lineStartIndex, lineSize);
                    textMesh.rectTransform.anchoredPosition = new Vector2(textMesh.rectTransform.anchoredPosition.x, -fontSize * i);

                    if (lineSize == 0) continue; //Without this check calling textMesh.UpdateVertexData pushes old data to the shader

                    //Apply colors
                    textMesh.ForceMeshUpdate();

                    TMP_TextInfo textInfo = textMesh.textInfo;
                    TMP_MeshInfo[] meshInfo = textInfo.meshInfo;
                    for (int h = 0; h < lineSize; h++)
                    {
                        TMP_CharacterInfo charInfo = textInfo.characterInfo[h];
                        //Skip past characters that are not visible. They do not have shader indexs and will cause issues if we try to apply colors to their vertexs
                        if (!charInfo.isVisible) continue;

                        int rawIndex = lineStartIndex + h;
                        int materialIndex = charInfo.materialReferenceIndex;
                        Color32[] colors = meshInfo[materialIndex].colors32;
                        Color32 targetColor = m_CharacterColors[rawIndex];

                        //Filter highlighted text for maximum visiblity
                        if (rawIndex >= m_StartSelectedIndex && rawIndex <= m_EndSelectedIndex)
                        {
                            targetColor = FilterHighlightedText(targetColor);
                        }

                        int vertexIndex = charInfo.vertexIndex;
                        colors[vertexIndex + 0] = targetColor;
                        colors[vertexIndex + 1] = targetColor;
                        colors[vertexIndex + 2] = targetColor;
                        colors[vertexIndex + 3] = targetColor;
                    }

                    textMesh.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
                }
            }
            else
            {
                //Pool leftover text meshs
                while(m_VisibleLineCount > linesLeft)
                {
                    int lastIndex = m_VisibleLineCount-1;
                    PoolTextMesh(m_VisibleLines[lastIndex]);
                    m_VisibleLines[lastIndex] = null;
                    m_VisibleLineCount--;
                }
            }

            CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild(this);

            //Update scrollbar
            if (m_ScrollBar != null)
            {
                float percent = 1.0f;
                if (m_LastLineIndex > 0)
                    percent = (float)m_TargetLineIndex / (float)m_LastLineIndex;
                m_ScrollBar.SetValueWithoutNotify(percent);
            }
        }

        private List<TextMeshProUGUI> m_TemporaryShiftArrayTexts = new List<TextMeshProUGUI>(16);

        public void ScrollUp(int lines=1)
        {
            if(targetLineIndex == 0 || lines <= 0 || (m_LineCount + 1) <= m_MaxVisibleLineCount)
                return;

            if(!isActiveAndEnabled)
            {
                m_IsDirty = true;
                return;
            }

            if(targetLineIndex - lines < 0)
            {
                lines = targetLineIndex;
            }

            m_TargetLineIndex -= lines;
            m_LastPossibleVisibleIndex = m_TargetLineIndex + m_MaxVisibleLineCount - 1;

            if (lines > m_MaxVisibleLineCount)
            {
                //Since were going to be changing every visible text mesh no optimizations can be found here so simply refresh
                Refresh();
                return;
            }

            //TODO I've spent too much time on this. This should be revisted to squeeze out as much performance as possible. So simply Refresh until then.
            Refresh();

            onTargetLineChanged?.Invoke();
        }

        public void ScrollDown(int lines=1)
        {
            if (targetLineIndex == m_LastLineIndex || lines == 0 || (m_LineCount + 1) <= m_MaxVisibleLineCount) 
                return;

            if(!isActiveAndEnabled)
            {
                m_IsDirty = true;
                return;
            }

            if(targetLineIndex + lines > m_LastLineIndex)
            {
                lines = m_LastLineIndex - targetLineIndex;
            }
            m_TargetLineIndex += lines;
            m_LastPossibleVisibleIndex = m_TargetLineIndex + m_MaxVisibleLineCount - 1;

            if (lines > m_MaxVisibleLineCount)
            {
                //Since were going to be changing every visible text mesh no optimizations can be found here so simply refresh
                Refresh();
                return;
            }

            //TODO I've spent too much time on this. This should be revisted to squeeze out as much performance as possible. So simply Refresh until then.
            Refresh();

            onTargetLineChanged?.Invoke();
        }


        void IScrollHandler.OnScroll(PointerEventData eventData)
        {
            if(eventData.scrollDelta.y > 0)
                ScrollUp(Mathf.Max(1, Mathf.RoundToInt(eventData.scrollDelta.y)));
            else if(eventData.scrollDelta.y < 0)
                ScrollDown(Mathf.Max(1, Mathf.RoundToInt(-eventData.scrollDelta.y)));
        }

        private void OnScrollBarValueChanged(float value)
        {
            float percent = 1.0f;
            if (m_LastLineIndex > 0)
                percent = value;

            m_TargetLineIndex = Mathf.FloorToInt(m_LastLineIndex * percent);
            m_LastPossibleVisibleIndex = m_TargetLineIndex + m_MaxVisibleLineCount - 1;

            if (isActiveAndEnabled)
                Refresh();
            else
                m_IsDirty = true;

            onTargetLineChanged?.Invoke();
        }

        //Create space at index for chars
        private unsafe void InsertRawText(int index, int count)
        {
            int newSize = m_RawLength + count;
            if(newSize > m_RawText.Length)
            {
                //Resize and move
                newSize = ((newSize / 64) + 1) * 64;
                char[] newCharArray = new char[newSize];
                Color32[] newColorArray = new Color32[newSize];

                fixed(char* srcPtr = &m_RawText[0])
                fixed(char* destPtr = &newCharArray[0])
                {
                    if(index > 0)
                    {
                        UnsafeUtility.MemCpy(destPtr, srcPtr, index * 2);
                    }

                    char* secondSrcPtr = srcPtr + index;
                    char* secondDestPtr = destPtr + index + count;

                    UnsafeUtility.MemCpy(secondDestPtr, secondSrcPtr, (m_RawLength - index) * 2);

                     
                }

                fixed(Color32* srcPtr = &m_CharacterColors[0])
                fixed(Color32* destPtr = &newColorArray[0])
                {
                    if(index > 0)
                    {
                        UnsafeUtility.MemCpy(destPtr, srcPtr, index * 4);
                    }

                    Color32* secondSrcPtr = srcPtr + index;
                    Color32* secondDestPtr = destPtr + index + count;

                    UnsafeUtility.MemCpy(secondDestPtr, secondSrcPtr, (m_RawLength - index) * 4);

                }

                m_RawText = newCharArray;
                m_CharacterColors = newColorArray;
            }
            else
            {
                //Just move
                fixed(char* srcPtr = &m_RawText[index])
                {
                    UnsafeUtility.MemMove(srcPtr + count, srcPtr, (m_RawLength - index) * 2); //2 is sizeof(char)
                }
                fixed(Color32* srcPtr = &m_CharacterColors[index])
                {
                    UnsafeUtility.MemMove(srcPtr + count, srcPtr, (m_RawLength - index) * 4); //4 is sizeof(Color32)
                }
            }

            m_RawLength += count;
        }

        private TextMeshProUGUI CreateTextMesh()
        {
            GameObject textObject = new GameObject("Text Line");
            textObject.transform.SetParent(transform);
            textObject.transform.localPosition = Vector3.zero;
            textObject.transform.localRotation = Quaternion.identity;
            textObject.transform.localScale = Vector3.one;

            TextMeshProUGUI textMesh = textObject.AddComponent<TextMeshProUGUI>();
            textMesh.font = font;
            textMesh.fontSize = fontSize;
            textMesh.richText = false;
            textMesh.horizontalAlignment = HorizontalAlignmentOptions.Left;
            textMesh.verticalAlignment = VerticalAlignmentOptions.Middle;
            textMesh.useMaxVisibleDescender = false;
            textMesh.parseCtrlCharacters = false;
            textMesh.isTextObjectScaleStatic = true;
            textMesh.enableWordWrapping = false;
            textMesh.overflowMode = TextOverflowModes.Overflow;
            textMesh.raycastTarget = false;

            textMesh.rectTransform.pivot = new Vector2(0.5f, 1.0f);
            textMesh.rectTransform.anchorMin = new Vector2(0, 1);
            textMesh.rectTransform.anchorMax = new Vector2(1, 1);
            textMesh.rectTransform.offsetMin = new Vector2(1.0f, 0.0f);
            textMesh.rectTransform.offsetMax = new Vector2(-1.0f, fontSize);

            return textMesh;
        }

        private void PoolTextMesh(TextMeshProUGUI textMesh)
        {
            m_TextMeshPool.Add(textMesh);
            textMesh.enabled = false;
            textMesh.gameObject.SetActive(false);
            textMesh.transform.SetParent(m_PoolParent);
        }

        private TextMeshProUGUI PopTextMesh()
        {
            if(m_TextMeshPool.Count == 0)
            {
                return CreateTextMesh();
            }

            int targetIndex = m_TextMeshPool.Count - 1;
            TextMeshProUGUI textMesh = m_TextMeshPool[targetIndex];
            textMesh.rectTransform.SetParent(transform);
            textMesh.font = font;
            textMesh.fontSize = fontSize;
            textMesh.color = Color.white;
            textMesh.rectTransform.offsetMin = new Vector2(1.0f, 0.0f);
            textMesh.rectTransform.offsetMax = new Vector2(-1.0f, fontSize);

            m_TextMeshPool.RemoveAt(targetIndex);
            textMesh.gameObject.SetActive(true);
            textMesh.enabled = true;

            return textMesh;
        }

        private void RecalculateLines()
        {
            m_LineSizes.Clear();
            m_LineStartIndexs.Clear();

            m_LineCount = 0;

            //Put back missing characters
            for (int i = 0; i < m_ReplacedCharacterIndexs.Count; i++)
            {
                m_RawText[m_ReplacedCharacterIndexs[i]] = m_OriginalReplacedCharacters[i];
            }

            m_ReplacedCharacterIndexs.Clear();
            m_OriginalReplacedCharacters.Clear();

            ParseText();

            if (m_LineCount > m_VisibleLines.Length)
            {
                //Resize visible line array
                int newSize = m_VisibleLines.Length;
                while (newSize < m_LineCount) newSize *= 2;
                Array.Resize(ref m_VisibleLines, newSize);
            }
        }

        protected override void OnRectTransformDimensionsChange()
        {
#if UNITY_EDITOR
            if(!Application.isPlaying || UnityEditor.EditorApplication.isPaused)
                return;
#endif

            //If the width changes, this means that the raw line count for each line actual could change
            if(m_RawLength > 0 && !Mathf.Approximately(m_LastRectSize.x, rectTransform.rect.width))
            {
                RecalculateLines();

                if(!Mathf.Approximately(m_LastRectSize.y, rectTransform.rect.height))
                {
                    m_MaxVisibleLineCount = Mathf.Max(1, Mathf.FloorToInt(rectTransform.rect.height / fontSize) + 1);
                }

                //Get the percent of the last line before setting the last line index
                float percent = 1.0f;
                if (m_LastLineIndex > 0)
                    percent = (float)m_TargetLineIndex / (float)m_LastLineIndex;

                m_LastLineIndex = Mathf.Clamp(m_LineCount - m_MaxVisibleLineCount + 1, 0, m_LineCount - 1);

                m_TargetLineIndex = Mathf.FloorToInt(m_LastLineIndex * percent);
                m_LastPossibleVisibleIndex = m_TargetLineIndex + m_MaxVisibleLineCount - 1;

                if(isActiveAndEnabled)
                    Refresh();
                else
                    m_IsDirty = true;

                onTargetLineChanged?.Invoke();
            }
            else if(!Mathf.Approximately(m_LastRectSize.y, rectTransform.rect.height))
            {
                m_MaxVisibleLineCount = Mathf.Max(1, Mathf.FloorToInt(rectTransform.rect.height / fontSize) + 1);

                //Get the percent of the last line before setting the last line index
                float percent = 1.0f;
                if (m_LastLineIndex > 0)
                    percent = (float)m_TargetLineIndex / (float)m_LastLineIndex;

                m_LastLineIndex = Mathf.Clamp(m_LineCount - m_MaxVisibleLineCount + 1, 0, m_LineCount - 1);

                m_TargetLineIndex = Mathf.FloorToInt(m_LastLineIndex * percent);
                m_LastPossibleVisibleIndex = m_TargetLineIndex + m_MaxVisibleLineCount - 1;

                if (m_RawLength > 0)
                {
                    if(isActiveAndEnabled)
                        Refresh();
                    else
                        m_IsDirty = true;
                }

                onTargetLineChanged?.Invoke();
            }

            m_LastRectSize = rectTransform.rect.size;
        }

        private void UploadTextColorData()
        {
            if(m_StartSelectedIndex >= 0 && m_EndSelectedIndex >= 0)
            {
                for (int i = 0; i < m_VisibleLineCount; i++)
                {
                    int targetIndex = m_TargetLineIndex + i;

                    int lineSize = m_LineSizes[targetIndex];
                    if (lineSize == 0) continue; //Without this check calling textMesh.UpdateVertexData pushes old data to the shader

                    int lineStartIndex = m_LineStartIndexs[targetIndex];

                    TextMeshProUGUI textMesh = m_VisibleLines[i];
                    textMesh.ForceMeshUpdate();

                    TMP_TextInfo textInfo = textMesh.textInfo;
                    TMP_MeshInfo[] meshInfo = textInfo.meshInfo;
                    for (int h = 0; h < lineSize; h++)
                    {
                        TMP_CharacterInfo charInfo = textInfo.characterInfo[h];
                        //Skip past characters that are not visible. They do not have shader indexs and will cause issues if we try to apply colors to their vertexs
                        if (!charInfo.isVisible) continue;
                        int rawIndex = lineStartIndex + h;
                        int materialIndex = charInfo.materialReferenceIndex;
                        Color32[] colors = meshInfo[materialIndex].colors32;
                        Color32 targetColor = m_CharacterColors[rawIndex];

                        //Filter highlighted text for maximum visiblity
                        if (rawIndex >= m_StartSelectedIndex && rawIndex <= m_EndSelectedIndex)
                        {
                            targetColor = FilterHighlightedText(targetColor);
                        }

                        int vertexIndex = charInfo.vertexIndex;
                        colors[vertexIndex + 0] = targetColor;
                        colors[vertexIndex + 1] = targetColor;
                        colors[vertexIndex + 2] = targetColor;
                        colors[vertexIndex + 3] = targetColor;
                    }

                    textMesh.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
                }
            }
            else
            {
                for (int i = 0; i < m_VisibleLineCount; i++)
                {
                    int targetIndex = m_TargetLineIndex + i;

                    int lineSize = m_LineSizes[targetIndex];
                    if (lineSize == 0) continue; //Without this check calling textMesh.UpdateVertexData pushes old data to the shader

                    int lineStartIndex = m_LineStartIndexs[targetIndex];

                    TextMeshProUGUI textMesh = m_VisibleLines[i];
                    textMesh.ForceMeshUpdate();

                    TMP_TextInfo textInfo = textMesh.textInfo;
                    TMP_MeshInfo[] meshInfo = textInfo.meshInfo;
                    for (int h = 0; h < lineSize; h++)
                    {
                        TMP_CharacterInfo charInfo = textInfo.characterInfo[h];
                        //Skip past characters that are not visible. They do not have shader indexs and will cause issues if we try to apply colors to their vertexs
                        if (!charInfo.isVisible) continue;
                        int rawIndex = lineStartIndex + h;
                        int materialIndex = charInfo.materialReferenceIndex;
                        Color32[] colors = meshInfo[materialIndex].colors32;
                        Color32 targetColor = m_CharacterColors[rawIndex];

                        int vertexIndex = charInfo.vertexIndex;
                        colors[vertexIndex + 0] = targetColor;
                        colors[vertexIndex + 1] = targetColor;
                        colors[vertexIndex + 2] = targetColor;
                        colors[vertexIndex + 3] = targetColor;
                    }

                    textMesh.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
                }
            }
        }

        public void SelectText(int startIndex, int endIndex)
        {
            if (m_RawLength == 0) return;

            if(m_Mesh == null)
            {
                m_Mesh = new Mesh();
            }

            if(endIndex < startIndex)
            {
                m_StartSelectedIndex = endIndex;
                m_EndSelectedIndex = startIndex;
            }
            else
            {
                m_StartSelectedIndex = startIndex;
                m_EndSelectedIndex = endIndex;
            }

            m_StartSelectedIndex = Mathf.Max(0, m_StartSelectedIndex);
            m_EndSelectedIndex = Mathf.Min(m_EndSelectedIndex, m_RawLength-1);

            m_HighLightRenderer.gameObject.SetActive(true);

            CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild(this);
        }

        public void SelectAll()
        {
            //This matches the SelectAll functionality of a browser's text area
            if(m_IsDragging)
            {
                m_DragStartCharIndex = 0;
            }

            SelectText(0, m_RawLength - 1);
        }

        public string GetSelectedText()
        {
            if(m_StartSelectedIndex < 0 || m_StartSelectedIndex > m_RawLength)
                return string.Empty;
            int count = m_EndSelectedIndex - m_StartSelectedIndex + 1;
            StringBuilder strBuilder = new StringBuilder(count);
            strBuilder.Append(m_RawText, m_StartSelectedIndex, count);
            
            for (int i = 0; i < m_ReplacedCharacterIndexs.Count; i++)
            {
                int ind = m_ReplacedCharacterIndexs[i];
                if (ind < m_StartSelectedIndex) continue;
                if (ind > m_EndSelectedIndex) break;

                strBuilder[ind-m_StartSelectedIndex] = m_OriginalReplacedCharacters[i];

                for (int h = i+1; h < m_ReplacedCharacterIndexs.Count; h++)
                {
                    ind = m_ReplacedCharacterIndexs[h];
                    if (ind > m_EndSelectedIndex) break;

                    strBuilder[ind-m_StartSelectedIndex] = m_OriginalReplacedCharacters[h];
                }

                break;
            }
            return strBuilder.ToString();
        }

        public void DeselectText()
        {
            if(m_StartSelectedIndex < 0 || m_EndSelectedIndex < 0)
                return;

            m_StartSelectedIndex = -1;
            m_EndSelectedIndex = -1;

            m_HighLightRenderer.gameObject.SetActive(false);

            DestroyImmediate(m_Mesh);

            CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild(this);

            UploadTextColorData();
        }

        private void GenerateHighlight()
        {
            if(rectTransform.rect.size.x <= 0 || rectTransform.rect.size.y <= 0)
            {
                //Cannot see the text correctly. Do not highlight.
                m_Mesh.Clear();
                m_HighLightRenderer.SetMesh(m_Mesh);
                return;
            }

            UIVertex vert = UIVertex.simpleVert;
            vert.uv0 = Vector2.zero;
            vert.color = m_HighlightBackgroundColor;

            //Find start index line
            int startCharIndex = m_StartSelectedIndex;
            int endCharIndex = m_EndSelectedIndex;
            int startLine = 1;
            int lastLine = 0;

            //Determine first line
            for (; startLine < m_LineCount; startLine++)
            {
                if (m_LineStartIndexs[startLine] > m_StartSelectedIndex)
                {
                    //We set last line here so that the loop later has less work to do
                    lastLine = startLine;

                    startLine--;
                    break;
                }
            }

            if(startLine == m_LineCount)
            {
                startLine--;
                if (startLine > m_LastPossibleVisibleIndex)
                {
                    //No work to be done if we cannot see the highlighted text
                    m_Mesh.Clear();
                    m_HighLightRenderer.SetMesh(m_Mesh);
                    return;
                }
                lastLine = startLine;
            }
            else
            {
                if (startLine < m_TargetLineIndex)
                {
                    startLine = m_TargetLineIndex;
                    startCharIndex = m_LineStartIndexs[startLine];
                }
                else if (startLine > m_LastPossibleVisibleIndex)
                {
                    //No work to be done if we cannot see the highlighted text
                    m_Mesh.Clear();
                    m_HighLightRenderer.SetMesh(m_Mesh);
                    return;
                }

                //Determine last line
                for (; lastLine < m_LineCount; lastLine++)
                {
                    if (m_LineStartIndexs[lastLine] > m_EndSelectedIndex)
                    {
                        lastLine--;
                        break;
                    }
                }

                if (lastLine == m_LineCount)
                {
                    lastLine--;
                }

                if (lastLine > m_LastPossibleVisibleIndex)
                {
                    lastLine = m_LastPossibleVisibleIndex;
                    endCharIndex = m_LineStartIndexs[lastLine] + m_LineSizes[lastLine] - 1;
                }
                else if (lastLine < m_TargetLineIndex)
                {
                    //No work to be done if we cannot see the highlighted text
                    m_Mesh.Clear();
                    m_HighLightRenderer.SetMesh(m_Mesh);
                    return;
                }
            }

            VertexHelper vertexHelper = new VertexHelper();
            Vector2 rectSize = rectTransform.rect.size;
            Vector2 halfSize = new Vector2(rectSize.x * 0.5f, rectSize.y * 0.5f);
            Vector2 startPosition = new Vector2(-halfSize.x, -halfSize.y);
            int targetLine = startLine - m_TargetLineIndex;

            if (startLine == lastLine)
            {
                //Determine width of the characters before the highlighted characters
                float currentLineWidth = 0;
                for (int i = m_LineStartIndexs[startLine]; i < startCharIndex; i++)
                {
                    currentLineWidth += GetCharWidth(m_RawText[i], currentLineWidth);
                }

                //Add left side verts
                float topY = startPosition.y + (targetLine * fontSize);
                float bottomY = startPosition.y + ((targetLine + 1) * fontSize);
                vert.position = new Vector3(startPosition.x + currentLineWidth, -topY);
                vertexHelper.AddVert(vert);
                vert.position = new Vector3(vert.position.x, -bottomY);
                vertexHelper.AddVert(vert);

                //Determine width of the highlighted characters

                if (m_LineCount > lastLine + 1 && endCharIndex == (m_LineStartIndexs[lastLine + 1] - 1))
                {
                    currentLineWidth = rectSize.x;
                }
                else
                {
                    for (int i = startCharIndex; i <= endCharIndex; i++)
                    {
                        currentLineWidth += GetCharWidth(m_RawText[i], currentLineWidth);
                    }
                }

                vert.position = new Vector3(startPosition.x + currentLineWidth, -bottomY);
                vertexHelper.AddVert(vert);
                vert.position = new Vector3(vert.position.x, -topY);
                vertexHelper.AddVert(vert);

                vertexHelper.AddTriangle(0, 1, 2);
                vertexHelper.AddTriangle(0, 2, 3);
            }
            else //Multi-line highlighting
            {
                //First line
                //Determine width of the characters before the highlighted characters
                float currentLineWidth = 0;
                for (int i = m_LineStartIndexs[startLine]; i < startCharIndex; i++)
                {
                    currentLineWidth += GetCharWidth(m_RawText[i], currentLineWidth);
                }

                float topY = -(startPosition.y + (targetLine * fontSize));

                vert.position = new Vector3(startPosition.x + currentLineWidth, topY);
                vertexHelper.AddVert(vert);
                vert.position = new Vector3(vert.position.x, topY - fontSize);
                vertexHelper.AddVert(vert);

                float lineWidth = m_VisibleLines[startLine - m_TargetLineIndex].textInfo.lineInfo[0].width;
                vert.position = new Vector3(startPosition.x + lineWidth, topY);
                vertexHelper.AddVert(vert);
                vert.position = new Vector3(vert.position.x, topY - fontSize);
                vertexHelper.AddVert(vert);

                vertexHelper.AddTriangle(0, 1, 2);
                vertexHelper.AddTriangle(1, 2, 3);

                //All lines in-between
                int midLineCount = lastLine - startLine;
                for (int i = 1; i < midLineCount; i++)
                {
                    lineWidth = m_VisibleLines[targetLine + i].textInfo.lineInfo[0].width;
                    float fontTopPositionY = topY - (fontSize * i);
                    vert.position = new Vector3(startPosition.x, fontTopPositionY);
                    vertexHelper.AddVert(vert);
                    vert.position = new Vector3(startPosition.x, vert.position.y - fontSize);
                    vertexHelper.AddVert(vert);
                    vert.position = new Vector3(startPosition.x + lineWidth, fontTopPositionY);
                    vertexHelper.AddVert(vert);
                    vert.position = new Vector3(vert.position.x, vert.position.y - fontSize);
                    vertexHelper.AddVert(vert);
                    int vertOffset = 4 * i;
                    int tri1 = 1 + vertOffset;
                    int tri2 = 2 + vertOffset;
                    vertexHelper.AddTriangle(0 + vertOffset, tri1, tri2);
                    vertexHelper.AddTriangle(tri1, tri2, 3 + vertOffset);
                }


                //Last line
                currentLineWidth = 0;
                if(m_LineCount > lastLine + 1 && endCharIndex == (m_LineStartIndexs[lastLine+1]-1))
                {
                    currentLineWidth = rectSize.x;
                }
                else
                {
                    for (int i = m_LineStartIndexs[lastLine]; i <= endCharIndex; i++)
                    {
                        currentLineWidth += GetCharWidth(m_RawText[i], currentLineWidth);
                    }
                }
                targetLine = lastLine - m_TargetLineIndex;
                topY = -(startPosition.y + (targetLine * fontSize));

                vert.position = new Vector3(startPosition.x, topY);
                vertexHelper.AddVert(vert);
                vert.position = new Vector3(vert.position.x, topY - fontSize);
                vertexHelper.AddVert(vert);

                vert.position = new Vector3(startPosition.x + currentLineWidth, topY);
                vertexHelper.AddVert(vert);
                vert.position = new Vector3(vert.position.x, topY - fontSize);
                vertexHelper.AddVert(vert);

                int vertOffsetL = 4 * midLineCount;
                int tri1L = 1 + vertOffsetL;
                int tri2L = 2 + vertOffsetL;
                vertexHelper.AddTriangle(0 + vertOffsetL, tri1L, tri2L);
                vertexHelper.AddTriangle(tri1L, tri2L, 3 + vertOffsetL);

            }

            vertexHelper.FillMesh(m_Mesh);
            vertexHelper.Dispose();

            m_HighLightRenderer.SetMesh(m_Mesh);


            //Reupload vertex colors
            UploadTextColorData();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Color32 FilterHighlightedText(Color32 textColor)
        {
            //NOTE: Right now we don't do anything with the text color but the option is there

            Color.RGBToHSV(highlightBackgroundColor, out float hue, out float saturation, out float value);
            if (value < 0.65f)
            {
                //Color.RGBToHSV(targetColor, out float targetHue, out float targetSaturation, out float targetValue);
                //targetColor = Color.HSVToRGB(targetHue, targetSaturation, 1.0f);
                return new Color32(255, 255, 255, 255);
            }
            else
            {
                //Color.RGBToHSV(targetColor, out float targetHue, out float targetSaturation, out float targetValue);
                //targetColor = Color.HSVToRGB(targetHue, targetSaturation, 0.05f);
                return new Color32(0, 0, 0, 255);
            }
        }

        /// <summary>
        /// Get the width of unicode character. Supports Tab(\t).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetCharWidth(char unicode, float currentLineWidth)
        {
            if (unicode == '\t')
            {
                float tabs = Mathf.Ceil(currentLineWidth / m_FontTabSize) * m_FontTabSize;
                if (tabs > currentLineWidth)
                {
                    return tabs - currentLineWidth;
                }
                else
                {
                    return m_FontTabSize;
                }
            }
            else if (font.characterLookupTable.TryGetValue(unicode, out TMP_Character character))
            {
                return character.glyph.metrics.horizontalAdvance * m_FontPointSizeScale + m_FontSpacingAdjustment * m_FontEmScale;
            }

            //The character was not found in the font's lookup table so it probably has no size or needs to be replaced by one of the missing character representations such as Square Char or Replacement Char
            return 0.0f;
        }

        public void Rebuild(CanvasUpdate executing)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying || UnityEditor.EditorApplication.isPaused)
                return;
#endif
            if(executing == CanvasUpdate.LatePreRender)
            {
                if(m_StartSelectedIndex >= 0 && isActiveAndEnabled)
                {
                    GenerateHighlight();
                }
            }
        }

        public void LayoutComplete() { }
        
        public void GraphicUpdateComplete() { }

        void IPointerMoveHandler.OnPointerMove(PointerEventData eventData)
        {
            if (m_IsDragging)
            {
                Cursor.SetIBeamCursor();

                RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, null, out Vector2 currentLocalPoint);
                //Convert local point
                currentLocalPoint = new Vector2(currentLocalPoint.x + (rectTransform.rect.width * 0.5f), currentLocalPoint.y - (rectTransform.rect.height * 0.5f));
                float lineFloatY = currentLocalPoint.y / -fontSize;
                int line = Mathf.FloorToInt(lineFloatY);
                int lineActual = m_TargetLineIndex + line;

                int start = -1;
                int end = -1;

                if (lineActual < 0)
                {
                    if (m_DragStartLine < 0 || m_DragStartPositionX < 0)
                    {
                        DeselectText();
                        return;
                    }

                    start = 0;
                    end = m_DragStartCharIndex;

                    if (m_DragStartLine < m_LineCount)
                    {
                        end--;
                    }
                }
                else if (lineActual >= m_LineCount)
                {
                    if (m_DragStartLine >= m_LineCount || m_DragStartCharIndex >= m_RawLength)
                    {
                        DeselectText();
                        return;
                    }
                    start = m_DragStartCharIndex;
                    end = m_RawLength - 1;
                }
                else
                {
                    int startCharIndex = m_LineStartIndexs[lineActual];
                    int endCharIndex = startCharIndex + m_LineSizes[lineActual] - 1;
                    float currentLineWidth = 0.0f;
                    int targetIndex = -1;
                    for (int i = startCharIndex; i <= endCharIndex; i++)
                    {
                        float charWidth = GetCharWidth(m_RawText[i], currentLineWidth);
                        if (currentLineWidth + (charWidth * 0.5f) >= currentLocalPoint.x)
                        {
                            targetIndex = i;
                            break;
                        }
                        currentLineWidth += charWidth;
                    }

                    if (targetIndex < 0)
                    {
                        targetIndex = endCharIndex + 1;
                    }

                    if (targetIndex < m_DragStartCharIndex)
                    {
                        start = targetIndex;
                        end = m_DragStartCharIndex-1;
                    }
                    else if(targetIndex > m_DragStartCharIndex)
                    {
                        start = m_DragStartCharIndex;
                        end = targetIndex - 1;
                    }
                    else
                    {
                        DeselectText();
                        return;
                    }
                }

                if (start != m_StartSelectedIndex || end != m_EndSelectedIndex)
                {
                    if (start == -1 || end == -1)
                    {
                        DeselectText();
                    }
                    else
                    {
                        SelectText(start, end);
                    }
                }
            }
            else if (highlightable && IsHoveringOverLines(eventData.position))
            {
                Cursor.SetIBeamCursor();
            }
            else
            {
                Cursor.SetArrowCursor();
            }
        }

        private float m_LastClickTime = -1.0f;

        void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
        {
            if (EventSystem.current.currentSelectedGameObject != gameObject)
            {
                EventSystem.current.SetSelectedGameObject(gameObject);
            }
            else
            {
                if (!highlightable || m_RawLength == 0) return;

                if (!m_PointerIsDown && Time.realtimeSinceStartup - m_LastClickTime <= m_DoubleClickTime)
                {
                    OnDoubleClick(eventData.position);
                    m_LastClickTime = -1.0f;
                    return;
                }

                m_PointerIsDown = true;

                m_LastClickTime = Time.realtimeSinceStartup;

                DeselectText();
                m_IsDragging = true;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, null, out Vector2 startLocalPoint);
                m_DragStartLine = m_TargetLineIndex + Mathf.FloorToInt(((startLocalPoint.y - (rectTransform.rect.height * 0.5f)) / -fontSize));
                m_DragStartPositionX = startLocalPoint.x + (rectTransform.rect.width * 0.5f);

                //Determine start char index
                if (m_DragStartLine < 0)
                {
                    m_DragStartCharIndex = 0;
                }
                else if(m_DragStartLine >= m_LineCount)
                {
                    m_DragStartCharIndex = m_RawLength - 1;
                }
                else
                {
                    m_DragStartCharIndex = -1;
                    int startCharIndex = m_LineStartIndexs[m_DragStartLine];
                    int endCharIndex = startCharIndex + m_LineSizes[m_DragStartLine] - 1;
                    float currentLineWidth = 0.0f;
                    for (int i = startCharIndex; i <= endCharIndex; i++)
                    {
                        float charWidth = GetCharWidth(m_RawText[i], currentLineWidth);
                        if(currentLineWidth + (charWidth * 0.5f) >= m_DragStartPositionX)
                        {
                            m_DragStartCharIndex = i;
                            break;
                        }
                        currentLineWidth += charWidth;
                    }

                    if(m_DragStartCharIndex < 0)
                    {
                        m_DragStartCharIndex = endCharIndex + 1;
                    }
                }
            }
        }

        private int m_DragStartCharIndex = -1;

        private void OnDoubleClick(Vector2 eventPosition)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventPosition, null, out Vector2 startLocalPoint);
            int line = m_TargetLineIndex + Mathf.FloorToInt(((startLocalPoint.y - (rectTransform.rect.height * 0.5f)) / -fontSize));
            float posX = startLocalPoint.x + (rectTransform.rect.width * 0.5f);
            int charIndex = -1;
            if (line < 0)
            {
                charIndex = 0;
            }
            else if (line >= m_LineCount)
            {
                charIndex = m_RawLength - 1;
            }
            else
            {
                int startCharIndex = m_LineStartIndexs[line];
                int endCharIndex = startCharIndex + m_LineSizes[line] - 1;
                float currentLineWidth = 0.0f;
                for (int i = startCharIndex; i <= endCharIndex; i++)
                {
                    float charWidth = GetCharWidth(m_RawText[i], currentLineWidth);
                    if (currentLineWidth + (charWidth * 0.5f) >= posX)
                    {
                        charIndex = i;
                        break;
                    }
                    currentLineWidth += charWidth;
                }

                if (charIndex < 0)
                {
                    if (line == m_LineCount - 1)
                    {
                        charIndex = endCharIndex;
                    }
                    else
                    {
                        charIndex = endCharIndex + 1;
                    }
                }
            }

            char startChar = m_RawText[charIndex];

            if (char.IsLetterOrDigit(startChar))
            {
                int start = charIndex;
                for (; start >= 0; start--)
                {
                    if (!char.IsLetterOrDigit(m_RawText[start]))
                    {
                        start++;
                        break;
                    }
                }
                int end = charIndex;
                for (; end < m_RawLength; end++)
                {
                    if (!char.IsLetterOrDigit(m_RawText[end]))
                    {
                        end--;
                        break;
                    }
                }
                SelectText(start, end);
            }
            else if(char.IsWhiteSpace(startChar))
            {
                int start = charIndex;
                for (; start >= 0; start--)
                {
                    if (!char.IsWhiteSpace(m_RawText[start]))
                    {
                        start++;
                        break;
                    }
                }
                int end = charIndex;
                for (; end < m_RawLength; end++)
                {
                    if (!char.IsWhiteSpace(m_RawText[end]))
                    {
                        end--;
                        break;
                    }
                }
                SelectText(start, end);
            }
            else
            {
                SelectText(charIndex, charIndex);
            }
        }

        float m_DragTimer = 0.0f;
        private Coroutine m_DragScrollCoroutine;
        private float m_DragTimeDelta = 0.0f;
        private bool m_DraggingDown;

        IEnumerator DragScroll()
        {
            while(m_IsDragging)
            {
                m_DragTimer += m_DragTimeDelta;
                if (m_DragTimer >= 1.0f)
                {
                    if(m_DraggingDown)
                        ScrollDown((int)(m_DragTimer / 1.0f));
                    else
                        ScrollUp((int)(m_DragTimer / 1.0f));

                    m_DragTimer %= 1.0f;
                }

                yield return null;
            }

            m_DragScrollCoroutine = null;
        }

        void IEndDragHandler.OnEndDrag(PointerEventData eventData)
        {
            EndDrag();
        }

        private void EndDrag()
        {
            m_IsDragging = false;
            if (m_DragScrollCoroutine != null)
            {
                StopCoroutine(m_DragScrollCoroutine);
                m_DragScrollCoroutine = null;
            }
        }

        bool IsHoveringOverLines(Vector2 screenPoint)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, null, out Vector2 localPoint))
            {
                if (m_VisibleLineCount >= m_MaxVisibleLineCount)
                {
                    return true;
                }
                else
                {
                    int lines = Mathf.FloorToInt((-localPoint.y + (rectTransform.rect.height * 0.5f)) / fontSize);
                    if (lines <= m_VisibleLineCount)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            Cursor.SetArrowCursor();
        }

        private Event m_ProcessingEvent = new Event();
        void IUpdateSelectedHandler.OnUpdateSelected(BaseEventData eventData)
        {
            while(Event.PopEvent(m_ProcessingEvent))
            {
                if(m_ProcessingEvent.type == EventType.KeyDown)
                {
                    EventModifiers currentEventModifiers = m_ProcessingEvent.modifiers;
                    bool ctrl = SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX ? (currentEventModifiers & EventModifiers.Command) != 0 : (currentEventModifiers & EventModifiers.Control) != 0;
                    bool shift = (currentEventModifiers & EventModifiers.Shift) != 0;
                    bool alt = (currentEventModifiers & EventModifiers.Alt) != 0;
                    bool ctrlOnly = ctrl && !alt && !shift;

                    switch (m_ProcessingEvent.keyCode)
                    {
                        case KeyCode.A: //Select All
                            if(ctrlOnly)
                                SelectAll();
                            break;
                        case KeyCode.C: //Copy
                            if(ctrlOnly && m_StartSelectedIndex >= 0 && m_EndSelectedIndex >= 0)
                            {
                                GUIUtility.systemCopyBuffer = GetSelectedText();
                            }
                            break;
                    }
                }

                continue;
                //REVIEW: Editor only?. Code reference from UGUI InputField. Is this only for things outside the gameview such as the inspector?
                /*
#if UNITY_EDITOR
                switch (m_ProcessingEvent.type)
                {
                    case EventType.ValidateCommand:
                    case EventType.ExecuteCommand:
                        switch (m_ProcessingEvent.commandName)
                        {
                            case "SelectAll":
                                SelectAll();
                                break;
                        }
                        break;
                }
#endif
                */
            }

            eventData.Use();
        }

        void IDeselectHandler.OnDeselect(BaseEventData eventData)
        {
            m_PointerIsDown = false;
            EndDrag();
        }

        void IPointerUpHandler.OnPointerUp(PointerEventData eventData)
        {
            m_PointerIsDown = false;
            EndDrag();
        }

        //For some reason without this ISelectHandler implementation the EventSystem does not keep this object selected when calling EventSystem.current.SetSelectedGameObject
        void ISelectHandler.OnSelect(BaseEventData eventData)
        {

        }

        //We can't do the drag scrolling inside PointerMove because PointerMove is not called when the pointer is outside the TextArea's RectTransform(or children's RectTransforms) while OnDrag is called when the pointer is anywhere on the screen.
        void IDragHandler.OnDrag(PointerEventData eventData)
        {
            if (m_IsDragging)
            {
                Cursor.SetIBeamCursor();

                RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, null, out Vector2 currentLocalPoint);
                //Convert local point
                currentLocalPoint = new Vector2(currentLocalPoint.x + (rectTransform.rect.width * 0.5f), currentLocalPoint.y - (rectTransform.rect.height * 0.5f));
                float lineFloatY = currentLocalPoint.y / -fontSize;
                int line = Mathf.FloorToInt(lineFloatY);
                int lineActual = m_TargetLineIndex + line;
                if (lineActual < m_TargetLineIndex && m_TargetLineIndex > 0)
                {
                    m_DragTimeDelta = Time.unscaledDeltaTime * m_DragScrollSpeed * -lineFloatY;
                    m_DraggingDown = false;

                    if (m_DragScrollCoroutine == null)
                    {
                        m_DragScrollCoroutine = StartCoroutine(DragScroll());
                    }
                }
                else if (lineActual > m_LastPossibleVisibleIndex && m_TargetLineIndex < m_LastLineIndex)
                {
                    m_DragTimeDelta = Time.unscaledDeltaTime * m_DragScrollSpeed * (lineFloatY - m_MaxVisibleLineCount);
                    m_DraggingDown = true;

                    if (m_DragScrollCoroutine == null)
                    {
                        m_DragScrollCoroutine = StartCoroutine(DragScroll());
                    }
                }
                else
                {
                    m_DragTimer = 0.0f;
                    if (m_DragScrollCoroutine != null)
                    {
                        StopCoroutine(m_DragScrollCoroutine);
                        m_DragScrollCoroutine = null;
                    }
                }
            }
        }
    }
}
