// AlwaysTooLate.Console (c) 2018-2019 Always Too Late.

using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace AlwaysTooLate.Console.Extensions
{
    internal class SelectableText
        : Selectable,
            IUpdateSelectedHandler,
            IBeginDragHandler,
            IDragHandler,
            IEndDragHandler,
            IPointerClickHandler,
            ICanvasElement,
            ILayoutElement
    {
        private const float VScrollSpeed = 0.10f;
        private static readonly char[] KSeparators = {' ', '.', ',', '\t', '\r', '\n'};

        private RectTransform _caretRectTrans;
        private Coroutine _blinkCoroutine;
        private float _blinkStartTime;
        private CanvasRenderer _cachedInputRenderer;
        protected int _caretPosition;
        protected int _caretSelectPosition;
        protected bool _caretVisible;
        protected UIVertex[] _cursorVerts;
        private Coroutine _dragCoroutine;
        private bool _dragPositionOutOfBounds;
        protected int _drawEnd;
        protected int _drawStart;
        private TextGenerator _inputTextCache;
        protected TouchScreenKeyboard _keyboard;
        private bool _preventFontCallback;
        private readonly Event _processingEvent = new Event();
        private bool _shouldActivateNextUpdate;
        private readonly bool _touchKeyboardAllowsInPlaceEditing = false;
        private bool _updateDrag;
        private WaitForSecondsRealtime _waitForSecondsRealtime;
        private Mesh _mesh;

        [FormerlySerializedAs("m_SelectionColor")] [SerializeField]
        private Color SelectionColor = new Color(168f / 255f, 206f / 255f, 255f / 255f, 192f / 255f);

        [SerializeField]
        [Multiline]
        protected string Text = string.Empty;

        [SerializeField]
        protected Text TextComponent;

        [SerializeField] [Range(1, 5)] 
        private int CaretWidth = 1;

        [SerializeField] 
        private bool CustomCaretColor;

        [SerializeField] [Range(0f, 4f)] 
        private float CaretBlinkRate = 0.85f;

        [SerializeField]
        private Color CaretColor = new Color(50f / 255f, 50f / 255f, 50f / 255f, 1f);

        public bool IsFocused { get; private set; }

        protected Mesh Mesh
        {
            get
            {
                if (_mesh == null)
                    _mesh = new Mesh();
                return _mesh;
            }
        }

        protected TextGenerator CachedInputTextGenerator
        {
            get
            {
                if (_inputTextCache == null)
                    _inputTextCache = new TextGenerator();

                return _inputTextCache;
            }
        }

        public string text
        {
            get => Text;
            set => SetText(value);
        }

        /// <summary>
        ///     The Text component that is going to be used to render the text to screen.
        /// </summary>
        public Text textComponent
        {
            get => TextComponent;
            set
            {
                if (TextComponent != null)
                {
                    TextComponent.UnregisterDirtyVerticesCallback(MarkGeometryAsDirty);
                    TextComponent.UnregisterDirtyVerticesCallback(UpdateLabel);
                    TextComponent.UnregisterDirtyMaterialCallback(UpdateCaretMaterial);
                }

                TextComponent = value;

                EnforceTextHOverflow();
                if (TextComponent != null)
                {
                    TextComponent.RegisterDirtyVerticesCallback(MarkGeometryAsDirty);
                    TextComponent.RegisterDirtyVerticesCallback(UpdateLabel);
                    TextComponent.RegisterDirtyMaterialCallback(UpdateCaretMaterial);
                }
            }
        }

        /// <summary>
        ///     The custom caret color used if customCaretColor is set.
        /// </summary>
        public Color caretColor
        {
            get => customCaretColor ? CaretColor : textComponent.color;
            set
            {
                CaretColor = value;
                MarkGeometryAsDirty();
            }
        }

        /// <summary>
        ///     Should a custom caret color be used or should the textComponent.color be used.
        /// </summary>
        public bool customCaretColor
        {
            get => CustomCaretColor;
            set
            {
                if (CustomCaretColor != value)
                {
                    CustomCaretColor = value;
                    MarkGeometryAsDirty();
                }
            }
        }

        public Color selectionColor
        {
            get => SelectionColor;
            set
            {
                SelectionColor = value;
                MarkGeometryAsDirty();
            }
        }

        protected int CaretPositionInternal
        {
            get => _caretPosition;
            set
            {
                _caretPosition = value;
                ClampPos(ref _caretPosition);
            }
        }

        protected int CaretSelectPositionInternal
        {
            get => _caretSelectPosition;
            set
            {
                _caretSelectPosition = value;
                ClampPos(ref _caretSelectPosition);
            }
        }

        private bool HasSelection => CaretPositionInternal != CaretSelectPositionInternal;

        protected SelectableText()
        {
            EnforceTextHOverflow();
        }

        /// <summary>
        ///     Capture the OnBeginDrag callback from the EventSystem and ensure we should listen to the drag events to follow.
        /// </summary>
        /// <param name="eventData">The data passed by the EventSystem</param>
        public virtual void OnBeginDrag(PointerEventData eventData)
        {
            if (!MayDrag(eventData))
                return;

            _updateDrag = true;
        }

        /// <summary>
        ///     Rebuild the input fields geometry. (caret and highlight).
        /// </summary>
        /// <param name="update">Which update loop we are in.</param>
        public virtual void Rebuild(CanvasUpdate update)
        {
            switch (update)
            {
                case CanvasUpdate.LatePreRender:
                    UpdateGeometry();
                    break;
            }
        }

        /// <summary>
        ///     See ICanvasElement.LayoutComplete. Does nothing by default.
        /// </summary>
        public virtual void LayoutComplete()
        {
        }

        /// <summary>
        ///     See ICanvasElement.GraphicUpdateComplete. Does nothing by default.
        /// </summary>
        public virtual void GraphicUpdateComplete()
        {
        }

        /// <summary>
        ///     If we are able to drag, try and select the character range underneath the bounding rect.
        /// </summary>
        /// <param name="eventData"></param>
        public virtual void OnDrag(PointerEventData eventData)
        {
            if (!MayDrag(eventData))
                return;

            var position = Vector2.zero;

            if (!GetRelativeMousePositionForDrag(eventData, ref position))
                return;

            Vector2 localMousePos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(textComponent.rectTransform, position,
                eventData.pressEventCamera, out localMousePos);
            CaretSelectPositionInternal = GetCharacterIndexFromPosition(localMousePos) + _drawStart;

            MarkGeometryAsDirty();

            _dragPositionOutOfBounds = !RectTransformUtility.RectangleContainsScreenPoint(textComponent.rectTransform,
                eventData.position, eventData.pressEventCamera);
            if (_dragPositionOutOfBounds && _dragCoroutine == null)
                _dragCoroutine = StartCoroutine(MouseDragOutsideRect(eventData));

            eventData.Use();
        }

        /// <summary>
        ///     Capture the OnEndDrag callback from the EventSystem and cancel the listening of drag events.
        /// </summary>
        /// <param name="eventData">The eventData sent by the EventSystem.</param>
        public virtual void OnEndDrag(PointerEventData eventData)
        {
            if (!MayDrag(eventData))
                return;

            _updateDrag = false;
        }

        /// <summary>
        ///     See ILayoutElement.CalculateLayoutInputHorizontal.
        /// </summary>
        public virtual void CalculateLayoutInputHorizontal()
        {
        }

        /// <summary>
        ///     See ILayoutElement.CalculateLayoutInputVertical.
        /// </summary>
        public virtual void CalculateLayoutInputVertical()
        {
        }

        /// <summary>
        ///     See ILayoutElement.minWidth.
        /// </summary>
        public virtual float minWidth => 0;

        /// <summary>
        ///     Get the displayed with of all input characters.
        /// </summary>
        public virtual float preferredWidth
        {
            get
            {
                if (textComponent == null)
                    return 0;
                var settings = textComponent.GetGenerationSettings(Vector2.zero);
                return textComponent.cachedTextGeneratorForLayout.GetPreferredWidth(Text, settings) /
                       textComponent.pixelsPerUnit;
            }
        }

        /// <summary>
        ///     See ILayoutElement.flexibleWidth.
        /// </summary>
        public virtual float flexibleWidth => -1;

        /// <summary>
        ///     See ILayoutElement.minHeight.
        /// </summary>
        public virtual float minHeight => 0;

        /// <summary>
        ///     Get the height of all the text if constrained to the height of the RectTransform.
        /// </summary>
        public virtual float preferredHeight
        {
            get
            {
                if (textComponent == null)
                    return 0;
                var settings =
                    textComponent.GetGenerationSettings(new Vector2(textComponent.rectTransform.rect.size.x, 0.0f));
                return textComponent.cachedTextGeneratorForLayout.GetPreferredHeight(Text, settings) /
                       textComponent.pixelsPerUnit;
            }
        }

        /// <summary>
        ///     See ILayoutElement.flexibleHeight.
        /// </summary>
        public virtual float flexibleHeight => -1;

        /// <summary>
        ///     See ILayoutElement.layoutPriority.
        /// </summary>
        public virtual int layoutPriority => 1;

        /// <summary>
        ///     What to do when the event system sends a pointer click Event
        /// </summary>
        /// <param name="eventData">The data on which to process</param>
        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            ActivateInputField();
        }

        /// <summary>
        ///     What to do when the event system sends a Update selected Event.
        /// </summary>
        /// <param name="eventData">The data on which to process.</param>
        public virtual void OnUpdateSelected(BaseEventData eventData)
        {
            if (!IsFocused)
                return;

            var consumedEvent = false;
            while (Event.PopEvent(_processingEvent))
            {
                if (_processingEvent.rawType == EventType.KeyDown)
                {
                    consumedEvent = true;
                    var shouldContinue = KeyPressed(_processingEvent);
                    if (shouldContinue == EditState.Finish) break;
                }

                switch (_processingEvent.type)
                {
                    case EventType.ValidateCommand:
                    case EventType.ExecuteCommand:
                        switch (_processingEvent.commandName)
                        {
                            case "SelectAll":
                                SelectAll();
                                consumedEvent = true;
                                break;
                        }

                        break;
                }
            }

            if (consumedEvent)
                UpdateLabel();

            eventData.Use();
        }

        private void SetText(string value, bool sendCallback = true)
        {
            if (text == value)
                return;
            if (value == null)
                value = "";

            value = value.Replace("\0", string.Empty); // remove embedded nulls
            Text = value;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                SendOnValueChangedAndUpdateLabel();
                return;
            }
#endif

            if (_keyboard != null)
                _keyboard.text = Text;

            if (_caretPosition > Text.Length)
                _caretPosition = _caretSelectPosition = Text.Length;
            else if (_caretSelectPosition > Text.Length)
                _caretSelectPosition = Text.Length;

            if (sendCallback)
                SendOnValueChanged();
            UpdateLabel();
        }

        /// <summary>
        ///     Clamp a value (by reference) between 0 and the current text length.
        /// </summary>
        /// <param name="pos">The input position to be clampped</param>
        protected void ClampPos(ref int pos)
        {
            if (pos < 0) pos = 0;
            else if (pos > text.Length) pos = text.Length;
        }

#if UNITY_EDITOR
        // Remember: This is NOT related to text validation!
        // This is Unity's own OnValidate method which is invoked when changing values in the Inspector.
        protected override void OnValidate()
        {
            base.OnValidate();
            EnforceContentType();
            EnforceTextHOverflow();

            //This can be invoked before OnEnabled is called. So we shouldn't be accessing other objects, before OnEnable is called.
            if (!IsActive())
                return;

            // fix case 1040277
            ClampPos(ref _caretPosition);
            ClampPos(ref _caretSelectPosition);


            UpdateLabel();
            if (IsFocused)
                SetCaretActive();
        }

#endif // if UNITY_EDITOR

        protected override void OnEnable()
        {
            base.OnEnable();
            if (Text == null)
                Text = string.Empty;
            _drawStart = 0;
            _drawEnd = Text.Length;

            // If we have a cached renderer then we had OnDisable called so just restore the material.
            if (_cachedInputRenderer != null)
                _cachedInputRenderer.SetMaterial(TextComponent.GetModifiedMaterial(Graphic.defaultGraphicMaterial),
                    Texture2D.whiteTexture);

            if (TextComponent != null)
            {
                TextComponent.RegisterDirtyVerticesCallback(MarkGeometryAsDirty);
                TextComponent.RegisterDirtyVerticesCallback(UpdateLabel);
                TextComponent.RegisterDirtyMaterialCallback(UpdateCaretMaterial);
                UpdateLabel();
            }
        }

        protected override void OnDisable()
        {
            // the coroutine will be terminated, so this will ensure it restarts when we are next activated
            _blinkCoroutine = null;

            if (TextComponent != null)
            {
                TextComponent.UnregisterDirtyVerticesCallback(MarkGeometryAsDirty);
                TextComponent.UnregisterDirtyVerticesCallback(UpdateLabel);
                TextComponent.UnregisterDirtyMaterialCallback(UpdateCaretMaterial);
            }

            CanvasUpdateRegistry.UnRegisterCanvasElementForRebuild(this);

            // Clear needs to be called otherwise sync never happens as the object is disabled.
            if (_cachedInputRenderer != null)
                _cachedInputRenderer.Clear();

            if (_mesh != null)
                DestroyImmediate(_mesh);
            _mesh = null;

            base.OnDisable();
        }

        private IEnumerator CaretBlink()
        {
            // Always ensure caret is initially visible since it can otherwise be confusing for a moment.
            _caretVisible = true;
            yield return null;

            while (IsFocused && CaretBlinkRate > 0)
            {
                // the blink rate is expressed as a frequency
                var blinkPeriod = 1f / CaretBlinkRate;

                // the caret should be ON if we are in the first half of the blink period
                var blinkState = (Time.unscaledTime - _blinkStartTime) % blinkPeriod < blinkPeriod / 2;
                if (_caretVisible != blinkState)
                {
                    _caretVisible = blinkState;
                    if (!HasSelection)
                        MarkGeometryAsDirty();
                }

                // Then wait again.
                yield return null;
            }

            _blinkCoroutine = null;
        }

        private void SetCaretVisible()
        {
            if (!IsFocused)
                return;

            _caretVisible = true;
            _blinkStartTime = Time.unscaledTime;
            SetCaretActive();
        }

        // SetCaretActive will not set the caret immediately visible - it will wait for the next time to blink.
        // However, it will handle things correctly if the blink speed changed from zero to non-zero or non-zero to zero.
        private void SetCaretActive()
        {
            if (!IsFocused)
                return;

            if (CaretBlinkRate > 0.0f)
            {
                if (_blinkCoroutine == null)
                    _blinkCoroutine = StartCoroutine(CaretBlink());
            }
            else
            {
                _caretVisible = true;
            }
        }

        private void UpdateCaretMaterial()
        {
            if (TextComponent != null && _cachedInputRenderer != null)
                _cachedInputRenderer.SetMaterial(TextComponent.GetModifiedMaterial(Graphic.defaultGraphicMaterial),
                    Texture2D.whiteTexture);
        }

        protected void OnFocus()
        {
        }

        /// <summary>
        ///     Highlight the whole InputField.
        /// </summary>
        /// <remarks>
        ///     Sets the caretPosition to the length of the text and caretSelectPos to 0.
        /// </remarks>
        protected void SelectAll()
        {
            CaretPositionInternal = text.Length;
            CaretSelectPositionInternal = 0;
        }

        /// <summary>
        ///     Move the caret index to end of text.
        /// </summary>
        /// <param name="shift">Only move the selection position to facilate selection</param>
        public void MoveTextEnd(bool shift)
        {
            var position = text.Length;

            if (shift)
            {
                CaretSelectPositionInternal = position;
            }
            else
            {
                CaretPositionInternal = position;
                CaretSelectPositionInternal = CaretPositionInternal;
            }

            UpdateLabel();
        }

        /// <summary>
        ///     Move the caret index to start of text.
        /// </summary>
        /// <param name="shift">Only move the selection position to facilate selection</param>
        public void MoveTextStart(bool shift)
        {
            var position = 0;

            if (shift)
            {
                CaretSelectPositionInternal = position;
            }
            else
            {
                CaretPositionInternal = position;
                CaretSelectPositionInternal = CaretPositionInternal;
            }

            UpdateLabel();
        }

        private bool InPlaceEditing()
        {
            return !TouchScreenKeyboard.isSupported || _touchKeyboardAllowsInPlaceEditing;
        }

        private void UpdateCaretFromKeyboard()
        {
            var selectionRange = _keyboard.selection;

            var selectionStart = selectionRange.start;
            var selectionEnd = selectionRange.end;

            var caretChanged = false;

            if (CaretPositionInternal != selectionStart)
            {
                caretChanged = true;
                CaretPositionInternal = selectionStart;
            }

            if (CaretSelectPositionInternal != selectionEnd)
            {
                CaretSelectPositionInternal = selectionEnd;
                caretChanged = true;
            }

            if (caretChanged)
            {
                _blinkStartTime = Time.unscaledTime;

                UpdateLabel();
            }
        }

        /// <summary>
        ///     Update the text based on input.
        /// </summary>
        protected virtual void LateUpdate()
        {
            // Only activate if we are not already activated.
            if (_shouldActivateNextUpdate)
            {
                if (!IsFocused)
                {
                    ActivateInputFieldInternal();
                    _shouldActivateNextUpdate = false;
                    return;
                }

                // Reset as we are already activated.
                _shouldActivateNextUpdate = false;
            }

            AssignPositioningIfNeeded();

            if (!IsFocused || InPlaceEditing())
                return;

            var val = _keyboard.text;

            if (Text != val)
                _keyboard.text = Text;
            else if (_keyboard.canGetSelection) UpdateCaretFromKeyboard();
        }

        private int GetUnclampedCharacterLineFromPosition(Vector2 pos, TextGenerator generator)
        {
            // transform y to local scale
            var y = pos.y * TextComponent.pixelsPerUnit;
            var lastBottomY = 0.0f;

            for (var i = 0; i < generator.lineCount; ++i)
            {
                var topY = generator.lines[i].topY;
                var bottomY = topY - generator.lines[i].height;

                // pos is somewhere in the leading above this line
                if (y > topY)
                {
                    // determine which line we're closer to
                    var leading = topY - lastBottomY;
                    if (y > topY - 0.5f * leading)
                        return i - 1;
                    return i;
                }

                if (y > bottomY)
                    return i;

                lastBottomY = bottomY;
            }

            // Position is after last line.
            return generator.lineCount;
        }

        /// <summary>
        ///     Given an input position in local space on the Text return the index for the selection cursor at this position.
        /// </summary>
        /// <param name="pos">Mouse position.</param>
        /// <returns>Character index with in value.</returns>
        protected int GetCharacterIndexFromPosition(Vector2 pos)
        {
            var gen = TextComponent.cachedTextGenerator;

            if (gen.lineCount == 0)
                return 0;

            var line = GetUnclampedCharacterLineFromPosition(pos, gen);
            if (line < 0)
                return 0;
            if (line >= gen.lineCount)
                return gen.characterCountVisible;

            var startCharIndex = gen.lines[line].startCharIdx;
            var endCharIndex = GetLineEndPosition(gen, line);

            for (var i = startCharIndex; i < endCharIndex; i++)
            {
                if (i >= gen.characterCountVisible)
                    break;

                var charInfo = gen.characters[i];
                var charPos = charInfo.cursorPos / TextComponent.pixelsPerUnit;

                var distToCharStart = pos.x - charPos.x;
                var distToCharEnd = charPos.x + charInfo.charWidth / TextComponent.pixelsPerUnit - pos.x;
                if (distToCharStart < distToCharEnd)
                    return i;
            }

            return endCharIndex;
        }

        private bool MayDrag(PointerEventData eventData)
        {
            return IsActive() &&
                   IsInteractable() &&
                   eventData.button == PointerEventData.InputButton.Left &&
                   TextComponent != null &&
                   InPlaceEditing();
        }

        private IEnumerator MouseDragOutsideRect(PointerEventData eventData)
        {
            while (_updateDrag && _dragPositionOutOfBounds)
            {
                var position = Vector2.zero;
                if (!GetRelativeMousePositionForDrag(eventData, ref position))
                    break;

                Vector2 localMousePos;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(textComponent.rectTransform, position,
                    eventData.pressEventCamera, out localMousePos);

                var rect = textComponent.rectTransform.rect;

                if (localMousePos.y > rect.yMax)
                    MoveUp(true, true);
                else if (localMousePos.y < rect.yMin)
                    MoveDown(true, true);

                UpdateLabel();
                var delay = VScrollSpeed;
                if (_waitForSecondsRealtime == null)
                    _waitForSecondsRealtime = new WaitForSecondsRealtime(delay);
                else
                    _waitForSecondsRealtime.waitTime = delay;
                yield return _waitForSecondsRealtime;
            }

            _dragCoroutine = null;
        }

        /// <summary>
        ///     The action to perform when the event system sends a pointer down Event.
        /// </summary>
        public override void OnPointerDown(PointerEventData eventData)
        {
            if (!MayDrag(eventData))
                return;

            EventSystem.current.SetSelectedGameObject(gameObject, eventData);

            var hadFocusBefore = IsFocused;
            base.OnPointerDown(eventData);

            if (!InPlaceEditing())
                if (_keyboard == null || !_keyboard.active)
                {
                    OnSelect(eventData);
                    return;
                }

            // Only set caret position if we didn't just get focus now.
            // Otherwise it will overwrite the select all on focus.
            if (hadFocusBefore)
            {
                Vector2 localMousePos;

                RectTransformUtility.ScreenPointToLocalPointInRectangle(textComponent.rectTransform,
                    eventData.pointerPressRaycast.screenPosition, eventData.pressEventCamera, out localMousePos);
                CaretSelectPositionInternal =
                    CaretPositionInternal = GetCharacterIndexFromPosition(localMousePos) + _drawStart;
            }

            UpdateLabel();
            eventData.Use();
        }


        /// <summary>
        ///     Process the Event and perform the appropriate action for that key.
        /// </summary>
        /// <param name="evt">The Event that is currently being processed.</param>
        /// <returns>If we should continue processing events or we have hit an end condition.</returns>
        protected EditState KeyPressed(Event evt)
        {
            var currentEventModifiers = evt.modifiers;
            var ctrl = SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX
                ? (currentEventModifiers & EventModifiers.Command) != 0
                : (currentEventModifiers & EventModifiers.Control) != 0;
            var shift = (currentEventModifiers & EventModifiers.Shift) != 0;
            var alt = (currentEventModifiers & EventModifiers.Alt) != 0;
            var ctrlOnly = ctrl && !alt && !shift;

            switch (evt.keyCode)
            {
                case KeyCode.Home:
                {
                    MoveTextStart(shift);
                    return EditState.Continue;
                }

                case KeyCode.End:
                {
                    MoveTextEnd(shift);
                    return EditState.Continue;
                }

                // Select All
                case KeyCode.A:
                {
                    if (ctrlOnly)
                    {
                        SelectAll();
                        return EditState.Continue;
                    }

                    break;
                }

                // Copy
                case KeyCode.C:
                {
                    if (ctrlOnly)
                    {
                        GUIUtility.systemCopyBuffer = GetSelectedString();
                        return EditState.Continue;
                    }

                    break;
                }
                case KeyCode.LeftArrow:
                {
                    MoveLeft(shift, ctrl);
                    return EditState.Continue;
                }

                case KeyCode.RightArrow:
                {
                    MoveRight(shift, ctrl);
                    return EditState.Continue;
                }

                case KeyCode.UpArrow:
                {
                    MoveUp(shift);
                    return EditState.Continue;
                }

                case KeyCode.DownArrow:
                {
                    MoveDown(shift);
                    return EditState.Continue;
                }
            }

            var c = evt.character;

            // Convert carriage return and end-of-text characters to newline.
            if (c == '\r' || c == 3)
                c = '\n';

            if (c == 0) UpdateLabel();
            return EditState.Continue;
        }

        private string GetSelectedString()
        {
            if (!HasSelection)
                return "";

            var startPos = CaretPositionInternal;
            var endPos = CaretSelectPositionInternal;

            // Ensure startPos is always less then endPos to make the code simpler
            if (startPos > endPos)
            {
                var temp = startPos;
                startPos = endPos;
                endPos = temp;
            }

            return text.Substring(startPos, endPos - startPos);
        }

        private int FindtNextWordBegin()
        {
            if (CaretSelectPositionInternal + 1 >= text.Length)
                return text.Length;

            var spaceLoc = text.IndexOfAny(KSeparators, CaretSelectPositionInternal + 1);

            if (spaceLoc == -1)
                spaceLoc = text.Length;
            else
                spaceLoc++;

            return spaceLoc;
        }

        private void MoveRight(bool shift, bool ctrl)
        {
            if (HasSelection && !shift)
            {
                // By convention, if we have a selection and move right without holding shift,
                // we just place the cursor at the end.
                CaretPositionInternal = CaretSelectPositionInternal =
                    Mathf.Max(CaretPositionInternal, CaretSelectPositionInternal);
                return;
            }

            int position;
            if (ctrl)
                position = FindtNextWordBegin();
            else
                position = CaretSelectPositionInternal + 1;

            if (shift)
                CaretSelectPositionInternal = position;
            else
                CaretSelectPositionInternal = CaretPositionInternal = position;
        }

        private int FindtPrevWordBegin()
        {
            if (CaretSelectPositionInternal - 2 < 0)
                return 0;

            var spaceLoc = text.LastIndexOfAny(KSeparators, CaretSelectPositionInternal - 2);

            if (spaceLoc == -1)
                spaceLoc = 0;
            else
                spaceLoc++;

            return spaceLoc;
        }

        private void MoveLeft(bool shift, bool ctrl)
        {
            if (HasSelection && !shift)
            {
                // By convention, if we have a selection and move left without holding shift,
                // we just place the cursor at the start.
                CaretPositionInternal = CaretSelectPositionInternal =
                    Mathf.Min(CaretPositionInternal, CaretSelectPositionInternal);
                return;
            }

            int position;
            if (ctrl)
                position = FindtPrevWordBegin();
            else
                position = CaretSelectPositionInternal - 1;

            if (shift)
                CaretSelectPositionInternal = position;
            else
                CaretSelectPositionInternal = CaretPositionInternal = position;
        }

        private int DetermineCharacterLine(int charPos, TextGenerator generator)
        {
            for (var i = 0; i < generator.lineCount - 1; ++i)
                if (generator.lines[i + 1].startCharIdx > charPos)
                    return i;

            return generator.lineCount - 1;
        }

        /// <summary>
        ///     Use cachedInputTextGenerator as the y component for the UICharInfo is not required
        /// </summary>
        private int LineUpCharacterPosition(int originalPos, bool goToFirstChar)
        {
            if (originalPos >= CachedInputTextGenerator.characters.Count)
                return 0;

            var originChar = CachedInputTextGenerator.characters[originalPos];
            var originLine = DetermineCharacterLine(originalPos, CachedInputTextGenerator);

            // We are on the first line return first character
            if (originLine <= 0)
                return goToFirstChar ? 0 : originalPos;

            var endCharIdx = CachedInputTextGenerator.lines[originLine].startCharIdx - 1;

            for (var i = CachedInputTextGenerator.lines[originLine - 1].startCharIdx; i < endCharIdx; ++i)
                if (CachedInputTextGenerator.characters[i].cursorPos.x >= originChar.cursorPos.x)
                    return i;
            return endCharIdx;
        }

        /// <summary>
        ///     Use cachedInputTextGenerator as the y component for the UICharInfo is not required
        /// </summary>
        private int LineDownCharacterPosition(int originalPos, bool goToLastChar)
        {
            if (originalPos >= CachedInputTextGenerator.characterCountVisible)
                return text.Length;

            var originChar = CachedInputTextGenerator.characters[originalPos];
            var originLine = DetermineCharacterLine(originalPos, CachedInputTextGenerator);

            // We are on the last line return last character
            if (originLine + 1 >= CachedInputTextGenerator.lineCount)
                return goToLastChar ? text.Length : originalPos;

            // Need to determine end line for next line.
            var endCharIdx = GetLineEndPosition(CachedInputTextGenerator, originLine + 1);

            for (var i = CachedInputTextGenerator.lines[originLine + 1].startCharIdx; i < endCharIdx; ++i)
                if (CachedInputTextGenerator.characters[i].cursorPos.x >= originChar.cursorPos.x)
                    return i;
            return endCharIdx;
        }

        private void MoveDown(bool shift)
        {
            MoveDown(shift, true);
        }

        private void MoveDown(bool shift, bool goToLastChar)
        {
            if (HasSelection && !shift)
                // If we have a selection and press down without shift,
                // set caret position to end of selection before we move it down.
                CaretPositionInternal = CaretSelectPositionInternal =
                    Mathf.Max(CaretPositionInternal, CaretSelectPositionInternal);

            var position = LineDownCharacterPosition(CaretSelectPositionInternal, goToLastChar);

            if (shift)
                CaretSelectPositionInternal = position;
            else
                CaretPositionInternal = CaretSelectPositionInternal = position;
        }

        private void MoveUp(bool shift)
        {
            MoveUp(shift, true);
        }

        private void MoveUp(bool shift, bool goToFirstChar)
        {
            if (HasSelection && !shift)
                // If we have a selection and press up without shift,
                // set caret position to start of selection before we move it up.
                CaretPositionInternal = CaretSelectPositionInternal =
                    Mathf.Min(CaretPositionInternal, CaretSelectPositionInternal);

            var position = LineUpCharacterPosition(CaretSelectPositionInternal, goToFirstChar);

            if (shift)
                CaretSelectPositionInternal = position;
            else
                CaretSelectPositionInternal = CaretPositionInternal = position;
        }

        private void UpdateTouchKeyboardFromEditChanges()
        {
            // Update the TouchKeyboard's text from edit changes
            // if in-place editing is allowed
            if (_keyboard != null && InPlaceEditing()) _keyboard.text = Text;
        }

        private void SendOnValueChangedAndUpdateLabel()
        {
            SendOnValueChanged();
            UpdateLabel();
        }

        private void SendOnValueChanged()
        {
            UISystemProfilerApi.AddMarker("InputField.value", this);
        }

        /// <summary>
        ///     Update the Text associated with this input field.
        /// </summary>
        protected void UpdateLabel()
        {
            if (TextComponent != null && TextComponent.font != null && !_preventFontCallback)
            {
                // TextGenerator.Populate invokes a callback that's called for anything
                // that needs to be updated when the data for that font has changed.
                // This makes all Text components that use that font update their vertices.
                // In turn, this makes the InputField that's associated with that Text component
                // update its label by calling this UpdateLabel method.
                // This is a recursive call we want to prevent, since it makes the InputField
                // update based on font data that didn't yet finish executing, or alternatively
                // hang on infinite recursion, depending on whether the cached value is cached
                // before or after the calculation.
                //
                // This callback also occurs when assigning text to our Text component, i.e.,
                // TextComponent.text = processed;

                _preventFontCallback = true;

                string fullText;
                if (EventSystem.current != null && gameObject == EventSystem.current.currentSelectedGameObject)
                    fullText = text.Substring(0, _caretPosition) + text.Substring(_caretPosition);
                else
                    fullText = text;

                var processed = fullText;

                var isEmpty = string.IsNullOrEmpty(fullText);

                // If not currently editing the text, set the visible range to the whole text.
                // The UpdateLabel method will then truncate it to the part that fits inside the Text area.
                // We can't do this when text is being edited since it would discard the current scroll,
                // which is defined by means of the _drawStart and _drawEnd indices.
                if (!IsFocused)
                {
                    _drawStart = 0;
                    _drawEnd = Text.Length;
                }

                if (!isEmpty)
                {
                    // Determine what will actually fit into the given line
                    var extents = TextComponent.rectTransform.rect.size;

                    var settings = TextComponent.GetGenerationSettings(extents);
                    settings.generateOutOfBounds = true;

                    CachedInputTextGenerator.PopulateWithErrors(processed, settings, gameObject);

                    SetDrawRangeToContainCaretPosition(CaretSelectPositionInternal);

                    processed = processed.Substring(_drawStart, Mathf.Min(_drawEnd, processed.Length) - _drawStart);

                    SetCaretVisible();
                }

                TextComponent.text = processed;
                MarkGeometryAsDirty();
                _preventFontCallback = false;
            }
        }

        private static int GetLineStartPosition(TextGenerator gen, int line)
        {
            line = Mathf.Clamp(line, 0, gen.lines.Count - 1);
            return gen.lines[line].startCharIdx;
        }

        private static int GetLineEndPosition(TextGenerator gen, int line)
        {
            line = Mathf.Max(line, 0);
            if (line + 1 < gen.lines.Count)
                return gen.lines[line + 1].startCharIdx - 1;
            return gen.characterCountVisible;
        }

        private void SetDrawRangeToContainCaretPosition(int caretPos)
        {
            // We don't have any generated lines generation is not valid.
            if (CachedInputTextGenerator.lineCount <= 0)
                return;

            // the extents gets modified by the pixel density, so we need to use the generated extents since that will be in the same 'space' as
            // the values returned by the TextGenerator.lines[x].height for instance.
            var extents = CachedInputTextGenerator.rectExtents.size;
            var lines = CachedInputTextGenerator.lines;
            var caretLine = DetermineCharacterLine(caretPos, CachedInputTextGenerator);

            if (caretPos > _drawEnd)
            {
                // Caret comes after drawEnd, so we need to move drawEnd to the end of the line with the caret
                _drawEnd = GetLineEndPosition(CachedInputTextGenerator, caretLine);
                var bottomY = lines[caretLine].topY - lines[caretLine].height;
                if (caretLine == lines.Count - 1)
                    // Remove interline spacing on last line.
                    bottomY += lines[caretLine].leading;
                var startLine = caretLine;
                while (startLine > 0)
                {
                    var topY = lines[startLine - 1].topY;
                    if (topY - bottomY > extents.y)
                        break;
                    startLine--;
                }

                _drawStart = GetLineStartPosition(CachedInputTextGenerator, startLine);
            }
            else
            {
                if (caretPos < _drawStart)
                    // Caret comes before drawStart, so we need to move drawStart to an earlier line start that comes before caret.
                    _drawStart = GetLineStartPosition(CachedInputTextGenerator, caretLine);

                var startLine = DetermineCharacterLine(_drawStart, CachedInputTextGenerator);
                var endLine = startLine;

                var topY = lines[startLine].topY;
                var bottomY = lines[endLine].topY - lines[endLine].height;

                if (endLine == lines.Count - 1)
                    // Remove interline spacing on last line.
                    bottomY += lines[endLine].leading;

                while (endLine < lines.Count - 1)
                {
                    bottomY = lines[endLine + 1].topY - lines[endLine + 1].height;

                    if (endLine + 1 == lines.Count - 1)
                        // Remove interline spacing on last line.
                        bottomY += lines[endLine + 1].leading;

                    if (topY - bottomY > extents.y)
                        break;
                    ++endLine;
                }

                _drawEnd = GetLineEndPosition(CachedInputTextGenerator, endLine);

                while (startLine > 0)
                {
                    topY = lines[startLine - 1].topY;
                    if (topY - bottomY > extents.y)
                        break;
                    startLine--;
                }

                _drawStart = GetLineStartPosition(CachedInputTextGenerator, startLine);
            }
        }

        private void MarkGeometryAsDirty()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying || PrefabUtility.IsPartOfPrefabAsset(gameObject))
                return;
#endif

            CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild(this);
        }

        private void UpdateGeometry()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif

            if (_cachedInputRenderer == null && TextComponent != null)
            {
                var go = new GameObject(transform.name + " Input Caret", typeof(RectTransform), typeof(CanvasRenderer));
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(TextComponent.transform.parent);
                go.transform.SetAsFirstSibling();
                go.layer = gameObject.layer;

                _caretRectTrans = go.GetComponent<RectTransform>();
                _cachedInputRenderer = go.GetComponent<CanvasRenderer>();
                _cachedInputRenderer.SetMaterial(TextComponent.GetModifiedMaterial(Graphic.defaultGraphicMaterial),
                    Texture2D.whiteTexture);

                // Needed as if any layout is present we want the caret to always be the same as the text area.
                go.AddComponent<LayoutElement>().ignoreLayout = true;

                AssignPositioningIfNeeded();
            }

            if (_cachedInputRenderer == null)
                return;

            OnFillVBO(Mesh);
            _cachedInputRenderer.SetMesh(Mesh);
        }

        private void AssignPositioningIfNeeded()
        {
            if (TextComponent != null && _caretRectTrans != null &&
                (_caretRectTrans.localPosition != TextComponent.rectTransform.localPosition ||
                 _caretRectTrans.localRotation != TextComponent.rectTransform.localRotation ||
                 _caretRectTrans.localScale != TextComponent.rectTransform.localScale ||
                 _caretRectTrans.anchorMin != TextComponent.rectTransform.anchorMin ||
                 _caretRectTrans.anchorMax != TextComponent.rectTransform.anchorMax ||
                 _caretRectTrans.anchoredPosition != TextComponent.rectTransform.anchoredPosition ||
                 _caretRectTrans.sizeDelta != TextComponent.rectTransform.sizeDelta ||
                 _caretRectTrans.pivot != TextComponent.rectTransform.pivot))
            {
                _caretRectTrans.localPosition = TextComponent.rectTransform.localPosition;
                _caretRectTrans.localRotation = TextComponent.rectTransform.localRotation;
                _caretRectTrans.localScale = TextComponent.rectTransform.localScale;
                _caretRectTrans.anchorMin = TextComponent.rectTransform.anchorMin;
                _caretRectTrans.anchorMax = TextComponent.rectTransform.anchorMax;
                _caretRectTrans.anchoredPosition = TextComponent.rectTransform.anchoredPosition;
                _caretRectTrans.sizeDelta = TextComponent.rectTransform.sizeDelta;
                _caretRectTrans.pivot = TextComponent.rectTransform.pivot;
            }
        }

        private void OnFillVBO(Mesh vbo)
        {
            using (var helper = new VertexHelper())
            {
                if (!IsFocused)
                {
                    helper.FillMesh(vbo);
                    return;
                }

                var roundingOffset = TextComponent.PixelAdjustPoint(Vector2.zero);
                if (!HasSelection)
                    GenerateCaret(helper, roundingOffset);
                else
                    GenerateHighlight(helper, roundingOffset);

                helper.FillMesh(vbo);
            }
        }

        private void GenerateCaret(VertexHelper vbo, Vector2 roundingOffset)
        {
            if (!_caretVisible)
                return;

            if (_cursorVerts == null) CreateCursorVerts();

            float width = CaretWidth;
            var adjustedPos = Mathf.Max(0, CaretPositionInternal - _drawStart);
            var gen = TextComponent.cachedTextGenerator;

            if (gen == null)
                return;

            if (gen.lineCount == 0)
                return;

            var startPosition = Vector2.zero;

            // Calculate startPosition
            if (adjustedPos < gen.characters.Count)
            {
                var cursorChar = gen.characters[adjustedPos];
                startPosition.x = cursorChar.cursorPos.x;
            }

            startPosition.x /= TextComponent.pixelsPerUnit;

            // TODO: Only clamp when Text uses horizontal word wrap.
            if (startPosition.x > TextComponent.rectTransform.rect.xMax)
                startPosition.x = TextComponent.rectTransform.rect.xMax;

            var characterLine = DetermineCharacterLine(adjustedPos, gen);
            startPosition.y = gen.lines[characterLine].topY / TextComponent.pixelsPerUnit;
            var height = gen.lines[characterLine].height / TextComponent.pixelsPerUnit;

            for (var i = 0; i < _cursorVerts.Length; i++)
                _cursorVerts[i].color = caretColor;

            _cursorVerts[0].position = new Vector3(startPosition.x, startPosition.y - height, 0.0f);
            _cursorVerts[1].position = new Vector3(startPosition.x + width, startPosition.y - height, 0.0f);
            _cursorVerts[2].position = new Vector3(startPosition.x + width, startPosition.y, 0.0f);
            _cursorVerts[3].position = new Vector3(startPosition.x, startPosition.y, 0.0f);

            if (roundingOffset != Vector2.zero)
                for (var i = 0; i < _cursorVerts.Length; i++)
                {
                    var uiv = _cursorVerts[i];
                    uiv.position.x += roundingOffset.x;
                    uiv.position.y += roundingOffset.y;
                }

            vbo.AddUIVertexQuad(_cursorVerts);

            var screenHeight = Screen.height;
            // Multiple display support only when not the main display. For display 0 the reported
            // resolution is always the desktops resolution since its part of the display API,
            // so we use the standard none multiple display method. (case 741751)
            var displayIndex = TextComponent.canvas.targetDisplay;
            if (displayIndex > 0 && displayIndex < Display.displays.Length)
                screenHeight = Display.displays[displayIndex].renderingHeight;

            // Calculate position of IME Window in screen space.
            Camera cameraRef;
            if (TextComponent.canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                cameraRef = null;
            else
                cameraRef = TextComponent.canvas.worldCamera;

            var cursorPosition = _cachedInputRenderer.gameObject.transform.TransformPoint(_cursorVerts[0].position);
            var screenPosition = RectTransformUtility.WorldToScreenPoint(cameraRef, cursorPosition);
            screenPosition.y = screenHeight - screenPosition.y;
        }

        private void CreateCursorVerts()
        {
            _cursorVerts = new UIVertex[4];

            for (var i = 0; i < _cursorVerts.Length; i++)
            {
                _cursorVerts[i] = UIVertex.simpleVert;
                _cursorVerts[i].uv0 = Vector2.zero;
            }
        }

        private void GenerateHighlight(VertexHelper vbo, Vector2 roundingOffset)
        {
            var startChar = Mathf.Max(0, CaretPositionInternal - _drawStart);
            var endChar = Mathf.Max(0, CaretSelectPositionInternal - _drawStart);

            // Ensure pos is always less then selPos to make the code simpler
            if (startChar > endChar)
            {
                var temp = startChar;
                startChar = endChar;
                endChar = temp;
            }

            endChar -= 1;
            var gen = TextComponent.cachedTextGenerator;

            if (gen.lineCount <= 0)
                return;

            var currentLineIndex = DetermineCharacterLine(startChar, gen);

            var lastCharInLineIndex = GetLineEndPosition(gen, currentLineIndex);

            var vert = UIVertex.simpleVert;
            vert.uv0 = Vector2.zero;
            vert.color = selectionColor;

            var currentChar = startChar;
            while (currentChar <= endChar && currentChar < gen.characterCount)
            {
                if (currentChar == lastCharInLineIndex || currentChar == endChar)
                {
                    var startCharInfo = gen.characters[startChar];
                    var endCharInfo = gen.characters[currentChar];
                    var startPosition = new Vector2(startCharInfo.cursorPos.x / TextComponent.pixelsPerUnit,
                        gen.lines[currentLineIndex].topY / TextComponent.pixelsPerUnit);
                    var endPosition =
                        new Vector2((endCharInfo.cursorPos.x + endCharInfo.charWidth) / TextComponent.pixelsPerUnit,
                            startPosition.y - gen.lines[currentLineIndex].height / TextComponent.pixelsPerUnit);

                    // Checking xMin as well due to text generator not setting position if char is not rendered.
                    if (endPosition.x > TextComponent.rectTransform.rect.xMax ||
                        endPosition.x < TextComponent.rectTransform.rect.xMin)
                        endPosition.x = TextComponent.rectTransform.rect.xMax;

                    var startIndex = vbo.currentVertCount;
                    vert.position = new Vector3(startPosition.x, endPosition.y, 0.0f) + (Vector3) roundingOffset;
                    vbo.AddVert(vert);

                    vert.position = new Vector3(endPosition.x, endPosition.y, 0.0f) + (Vector3) roundingOffset;
                    vbo.AddVert(vert);

                    vert.position = new Vector3(endPosition.x, startPosition.y, 0.0f) + (Vector3) roundingOffset;
                    vbo.AddVert(vert);

                    vert.position = new Vector3(startPosition.x, startPosition.y, 0.0f) + (Vector3) roundingOffset;
                    vbo.AddVert(vert);

                    vbo.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
                    vbo.AddTriangle(startIndex + 2, startIndex + 3, startIndex + 0);

                    startChar = currentChar + 1;
                    currentLineIndex++;

                    lastCharInLineIndex = GetLineEndPosition(gen, currentLineIndex);
                }

                currentChar++;
            }
        }

        public void ActivateInputField()
        {
            if (TextComponent == null || TextComponent.font == null || !IsActive() || !IsInteractable())
                return;

            if (IsFocused)
                if (_keyboard != null && !_keyboard.active)
                {
                    _keyboard.active = true;
                    _keyboard.text = Text;
                }

            _shouldActivateNextUpdate = true;
        }

        private void ActivateInputFieldInternal()
        {
            if (EventSystem.current == null)
                return;

            if (EventSystem.current.currentSelectedGameObject != gameObject)
                EventSystem.current.SetSelectedGameObject(gameObject);

            // Perform normal OnFocus routine if platform supports it
            if (!TouchScreenKeyboard.isSupported || _touchKeyboardAllowsInPlaceEditing) OnFocus();
            IsFocused = true;
            SetCaretVisible();
            UpdateLabel();
        }

        /// <summary>
        ///     What to do when the event system sends a submit Event.
        /// </summary>
        /// <param name="eventData">The data on which to process</param>
        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            ActivateInputField();
        }

        private void EnforceContentType()
        {
            EnforceTextHOverflow();
        }

        private void EnforceTextHOverflow()
        {
            if (TextComponent != null)
                TextComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        /// <summary>
        ///     Converts the current drag position into a relative position for the display.
        /// </summary>
        /// <param name="eventData"></param>
        /// <param name="position"></param>
        /// <returns>Returns true except when the drag operation is not on the same display as it originated</returns>
        public static bool GetRelativeMousePositionForDrag(PointerEventData eventData, ref Vector2 position)
        {
#if UNITY_EDITOR
            position = eventData.position;
#else
            int pressDisplayIndex = eventData.pointerPressRaycast.displayIndex;
            var relativePosition = Display.RelativeMouseAt(eventData.position);
            int currentDisplayIndex = (int)relativePosition.z;

            // Discard events on a different display.
            if (currentDisplayIndex != pressDisplayIndex)
                return false;

            // If we are not on the main display then we must use the relative position.
            position = pressDisplayIndex != 0 ? (Vector2)relativePosition : eventData.position;
#endif
            return true;
        }

        protected enum EditState
        {
            Continue,
            Finish
        }
    }
}