// AlwaysTooLate.Console (c) 2018-2019 Always Too Late.

using AlwaysTooLate.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AlwaysTooLate.Console
{
    public class ConsoleHandler : MonoBehaviour
    {
        private GameObjectPool _textPool;
        public Animation Animation;
        public TMP_InputField CommandField;
        public Transform Content;

        public TMP_FontAsset Font;

        public int FontSize = 14;
        public Transform Highlights;
        public ScrollRect ScrollRect;

        public bool IsEnteringCommand => CommandField.isFocused;

        public string CurrentCommand
        {
            get => CommandField.text;
            set => CommandField.text = value;
        }

        protected void Start()
        {
            // Setup GameObject pool (+32 elems for highlights)
            _textPool = new GameObjectPool(ConsoleManager.Instance.MaxMessages + 32, new[]
            {
                typeof(TextMeshProUGUI), typeof(ContentSizeFitter)
            });

            gameObject.SetActive(false);
            Highlights.gameObject.SetActive(false);
        }

        protected void OnDestroy()
        {
            // Dispose the GameObject pool
            _textPool.Dispose();
            _textPool = null;
        }

        public void Open()
        {
            gameObject.SetActive(true);
            OnOpenBegin();
            SetFocus();
            Animation.Play("ConsoleOpen");
        }

        public void Close()
        {
            OnCloseBegin();
            Animation.Play("ConsoleClose");
        }

        public void ClearCommand()
        {
            CommandField.text = string.Empty;
        }

        public void ClearConsole()
        {
            while (Content.childCount > 0)
                _textPool.Release(Content.GetChild(0).gameObject);
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
            for (var i = 0; i < Highlights.childCount; i++)
            {
                var textObject = Highlights.GetChild(i);
                var textComponent = textObject.GetComponent<TextMeshProUGUI>();
                textComponent.color = index == i ? Color.yellow : Color.white;
            }
        }

        public void SetHighlights(string[] highlights, string markText = "")
        {
            while (Highlights.childCount > 0)
                _textPool.Release(Highlights.GetChild(0).gameObject);

            if (highlights == null || highlights.Length == 0)
            {
                Highlights.gameObject.SetActive(false);
                return;
            }

            Highlights.gameObject.SetActive(true);

            foreach (var highlight in highlights)
            {
                // Setup text rect
                var textObject = _textPool.Acquire();

                // Setup transform
                var textTransform = (RectTransform) textObject.transform;
                textTransform.SetParent(Highlights);
                textTransform.sizeDelta = new Vector2(textTransform.sizeDelta.x, 20);

                // Setup text
                var textComponent = textObject.GetComponent<TextMeshProUGUI>();
                textComponent.text = RichTextExtensions.ColorInnerString(highlight, markText, "green");
                textComponent.color = Color.white;
                textComponent.font = Font;
                textComponent.fontSize = FontSize;
                textComponent.richText = true; // Enable rich text

                // Setup content size fitter
                var textFitter = textObject.GetComponent<ContentSizeFitter>();
                textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            var csf = Highlights.GetComponent<ContentSizeFitter>();
            csf.SetLayoutVertical();
        }

        public void AddLine(string text)
        {
            AddLine(text, Color.white);
        }

        public void AddLine(string text, Color color)
        {
            if (_textPool == null)
                return;

            // Release old children to maintain the desired message count
            while (Content.childCount >= ConsoleManager.Instance.MaxMessages)
                _textPool.Release(Content.GetChild(0).gameObject);

            // Setup text rect
            var textObject = _textPool.Acquire();

            // Setup transform
            var textTransform = (RectTransform) textObject.transform;
            textTransform.SetParent(Content);
            textTransform.sizeDelta = new Vector2(textTransform.sizeDelta.x, 20);

            // Setup text
            var textComponent = textObject.GetComponent<TextMeshProUGUI>();
            textComponent.text = text;
            textComponent.color = color;
            textComponent.font = Font;
            textComponent.fontSize = FontSize;
            textComponent.richText = true; // Enable rich text

            // Setup content size fitter
            var textFitter = textObject.GetComponent<ContentSizeFitter>();
            textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // TODO: Fix scroll to bottom issue.
            ScrollRect.verticalNormalizedPosition = 0.0f;
        }

        protected void OnOpenBegin()
        {
            gameObject.SetActive(true);
            SetHighlights(null);
        }

        protected void OnOpenEnd() // Called from animation event
        {
        }

        protected void OnCloseBegin()
        {
            ClearCommand();
            SetHighlights(null);
        }

        protected void OnCloseEnd() // Called from animation event
        {
            ClearCommand();
            gameObject.SetActive(false);
        }

        public void OnTypeCommand(string command)
        {
            ConsoleManager.Instance.OnCommandUpdate(command);
        }

        public void OnInputCommand(string command)
        {
            ConsoleManager.Instance.OnCommandEnter(command);
        }
    }
}