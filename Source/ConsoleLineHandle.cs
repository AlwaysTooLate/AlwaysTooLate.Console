// AlwaysTooLate.Console (c) 2018-2020 Always Too Late.

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace AlwaysTooLate.Console
{
    public class ConsoleLineHandle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public TMP_Text Text;
        public Image Image;
        public Button Button;
        public RectTransform RectTransform;
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
    }
}
