// AlwaysTooLate.Console (c) 2018-2019 Always Too Late.

using UnityEngine;
using UnityEngine.UI;

namespace AlwaysTooLate.Console
{
    public class ConsoleHandler : MonoBehaviour
    {
        public InputField CommandField;
        public Animation Animation;

        private void Start()
        {
            gameObject.SetActive(false);
        }

        public void Open()
        {
            gameObject.SetActive(true);

            CommandField.Select();
            CommandField.ActivateInputField();

            Animation.Play("ConsoleOpen");
        }

        public void Close()
        {
            Animation.Play("ConsoleClose");
        }

        public void OnOpenBegin() // Called from animation event
        {
            gameObject.SetActive(true);
        }

        public void OnOpenEnd() // Called from animation event
        {
        }

        public void OnCloseBegin() // Called from animation event
        {
        }

        public void OnCloseEnd() // Called from animation event
        {
            gameObject.SetActive(false);
        }

        public void OnTypeCommand(string command)
        {

        }

        public void OnInputCommand(string command)
        {

        }
    }
}