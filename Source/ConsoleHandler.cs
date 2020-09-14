// AlwaysTooLate.Console (c) 2018-2019 Always Too Late.

using AlwaysTooLate.Core;
using TMPro;
using UnityEngine;

namespace AlwaysTooLate.Console
{
    public class ConsoleHandler : MonoBehaviour
    {
        public Animation Animation;
        public TMP_InputField CommandField;
        public TMP_InputField ConsoleOutput;

        public TMP_FontAsset Font;

        public int FontSize = 14;
        public Transform Highlights;
        public TMP_Text HighlightsText;

        public bool IsEnteringCommand => CommandField.isFocused;

        public string CurrentCommand
        {
            get => CommandField.text;
            set => CommandField.text = value;
        }

        protected void Start()
        {
            gameObject.SetActive(false);
            Highlights.gameObject.SetActive(false);
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
            ConsoleOutput.text = string.Empty;
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
            HighlightsText.text = string.Empty;

            if (highlights == null || highlights.Length == 0)
            {
                Highlights.gameObject.SetActive(false);
                return;
            }

            Highlights.gameObject.SetActive(true);

            foreach (var highlight in highlights)
            {
                var text = RichTextExtensions.ColorInnerString(highlight, markText, "green");
                HighlightsText.text += $"{text}\n";
            }
        }

        public void AddLine(string text)
        {
            AddLine(text, Color.white);
        }

        public void AddLine(string text, Color color)
        {
            ConsoleOutput.text += "\n" + text;
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