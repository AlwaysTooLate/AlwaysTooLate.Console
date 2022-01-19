// AlwaysTooLate.Console (c) 2018-2022 Always Too Late.

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace AlwaysTooLate.Console
{
    public class ConsoleLineHandle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public TMP_Text Text;
        public Image Image;
        public RectTransform RectTransform;
        public string Clipboard;
        private bool _isHighlighted;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_isHighlighted)
                return;
            Image.color *= 2f;
            _isHighlighted = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_isHighlighted)
                return;
            Image.color /= 2f;
            _isHighlighted = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (string.IsNullOrWhiteSpace(Clipboard))
                return;
            var textEditor = new TextEditor
            {
                text = Clipboard
            };
            textEditor.SelectAll();
            textEditor.Copy();
        }
    }
}
