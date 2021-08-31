// AlwaysTooLate.Console (c) 2018-2020 Always Too Late.

using System;
using System.Collections.Generic;
using System.Linq;
using AlwaysTooLate.Core;
using AlwaysTooLate.Core.Pooling;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AlwaysTooLate.Console
{
    [DefaultExecutionOrder(-9999)]
    public class ConsoleHandler : MonoBehaviour
    {
        private int _totalLines;

        public TMP_InputField CommandField;
        public ScrollRect ScrollRect;

        public RectTransform Content;

        public RectTransform Highlights;
        public TMP_Text HighlightsText;

        public GameObject LinePrefab;
        public float LineHeight = 30.0f;
        public float ScrollbarWidth = 20.0f;

        public int MaxLines = 256;
        public bool LimitLineCount = true;

        public Color LineAColor;
        public Color LineBColor;

        public bool IsEnteringCommand => CommandField.isFocused;

        private int _screenWidth;
        private int _numLines;
        private string[] _currentHighlights;
        private string _currentMarkText;
        private int _selectedHighlight;
        private GameObjectPool<ConsoleLineHandle> _pool;
        private readonly List<ConsoleLineHandle> _lines = new List<ConsoleLineHandle>();

        public string CurrentCommand
        {
            get => CommandField.text;
            set => CommandField.text = value;
        }

        protected void Start()
        {
            _screenWidth = Screen.width;

            CommandField.onValueChanged.AddListener(OnTypeCommand);
            CommandField.onSubmit.AddListener(OnInputCommand);

            gameObject.SetActive(false);
            Highlights.gameObject.SetActive(false);
            _pool = new GameObjectPool<ConsoleLineHandle>(LinePrefab, 1024);
        }

        protected void OnDestroy()
        {
            Cleanup();
        }

        protected void Update()
        {
            UpdateScreenSize();
        }

        public void Open()
        {
            gameObject.SetActive(true);
            SetHighlights(null);
            SetFocus();
        }

        public void Close()
        {
            gameObject.SetActive(false);
            SetHighlights(null);
            ClearCommand();
        }

        public void ClearCommand()
        {
            CommandField.text = string.Empty;
        }

        public void ClearConsole()
        {
            // Release all lines
            foreach (var line in _lines)
            {
                if (!line) continue;
                _pool.Release(line);
            }

            // Cleanup and resize scroll's content
            _numLines = 0;
            _totalLines = 0;
            UpdateContentSize();
            _lines.Clear();
        }

        public void SetFocus()
        {
            CommandField.Select();
            CommandField.ActivateInputField();
        }

        public void MoveCaretToEnd()
        {
            CommandField.MoveTextEnd(false);
            SetFocus();
        }

        public void SelectHighlight(int index)
        {
            _selectedHighlight = index;
            SetHighlights(_currentHighlights, _currentMarkText);
        }

        public void SetHighlights(string[] highlights, string markText = "")
        {
            _currentHighlights = highlights;
            _currentMarkText = markText;

            HighlightsText.text = string.Empty;

            if (highlights is null || highlights.Length == 0)
            {
                Highlights.gameObject.SetActive(false);
                return;
            }

            Highlights.gameObject.SetActive(true);

            for(int i = 0; i < highlights.Length; i++)
            {
                var highlight = highlights[i];
                HighlightsText.text += _selectedHighlight == i
                    ? $"<color=yellow>{highlight}</color>\n"
                    : RichTextExtensions.ColorInnerString(highlight, markText, "green") + "\n";
            }
            var highlightsRect = HighlightsText.rectTransform;
            var width = HighlightsText.preferredWidth + highlightsRect.offsetMin.x - highlightsRect.offsetMax.x;
            Highlights.sizeDelta = new Vector2(width, 20.0f + highlights.Length * 18.0f);
        }

        public void AddLine(string text)
        {
            AddLine(text, Color.white, null);
        }

        public void AddLine(string text, Color color, string clipboard)
        {
            if (_pool == null) return;

            var line = _pool.Acquire();

            if (line == null) return;

            line.Clipboard = clipboard;

            text = text.Replace("\n", "").Replace("\r", "");
            line.RectTransform.SetParent(Content);
            line.RectTransform.anchoredPosition = new Vector2(0.0f, -_numLines * LineHeight);
            line.RectTransform.sizeDelta = new Vector2(Screen.width - ScrollbarWidth, 0f);
            //Getting lines to ajust line height
            var lineCount = line.Text.GetTextInfo(text).lineCount;
            line.RectTransform.sizeDelta = new Vector2(Screen.width - ScrollbarWidth, LineHeight * lineCount);
            line.Image.color = GetColorForCurrentLine();
            line.Text.text = text;
            line.Text.color = color;
            _lines.Add(line);
            _numLines += lineCount;
            _totalLines += lineCount;

            if (LimitLineCount && _lines.Count > MaxLines)
            {
                var oldLine = _lines.First();
                _lines.RemoveAt(0);
                _pool.Release(oldLine);
                _numLines--;

                PositionLines();
            }

            UpdateContentSize();
            ScrollToBottom();
        }

        public void OnTypeCommand(string command)
        {
            ConsoleManager.Instance.OnCommandUpdate(command);
        }

        public void OnInputCommand(string command)
        {
            ConsoleManager.Instance.OnCommandEnter(command);
        }

        private void UpdateScreenSize()
        {
            // Resize all lines, when the screen width changes.
            // This is required, because we're not relying on the Unity's layout system.
            if (_screenWidth != Screen.width)
            {
                _screenWidth = Screen.width;
                ResizeLines();
            }
        }

        private void ResizeLines()
        {
            var resize_lines = false;

            foreach (var line in _lines)
            {
                var prevLineCount = line.Text.textInfo.lineCount;
                line.RectTransform.sizeDelta = new Vector2(_screenWidth - ScrollbarWidth, 0f);
                var curLineCount = line.Text.GetTextInfo(line.Text.text).lineCount;
                line.RectTransform.sizeDelta = new Vector2(_screenWidth - ScrollbarWidth, LineHeight * curLineCount);
                if (prevLineCount == curLineCount)
                    continue;
                _totalLines += curLineCount - prevLineCount;
                resize_lines = true;
            }

            if (resize_lines)
            {
                PositionLines();
                UpdateContentSize();
            }
        }

        private void PositionLines()
        {
            _numLines = 0;
            foreach (var line in _lines)
            {
                line.RectTransform.anchoredPosition = new Vector2(0.0f, -_numLines * LineHeight);
                _numLines += line.Text.textInfo.lineCount;
            }
        }

        private void Cleanup()
        {
            if (_pool == null) return;

            // Release all lines
            foreach (var line in _lines)
            {
                if (!line) continue;
                _pool.Release(line);
            }

            // Dispose the pool
            _pool.Dispose();
            _pool = null;
        }

        private void UpdateContentSize()
        {
            Content.sizeDelta = new Vector2(Screen.width, Mathf.Max(_numLines * LineHeight, LineHeight));
        }

        private Color GetColorForCurrentLine()
        {
            return _lines.Count % 2 == 0 ? LineAColor : LineBColor;
        }

        private void ScrollToBottom()
        {
            ScrollRect.normalizedPosition = new Vector2(0, 0);
        }
    }
}