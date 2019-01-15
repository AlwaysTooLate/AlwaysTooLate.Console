// AlwaysTooLate.Console (c) 2018-2019 Always Too Late.

using AlwaysTooLate.Core;
using UnityEngine;

namespace AlwaysTooLate.Console
{
    /// <summary>
    /// ConsoleManager class. Implements console behavior.
    /// Should be initialized on main (entry) scene.
    /// </summary>
    public class ConsoleManager : BehaviourSingleton<ConsoleManager>
    {
        public const string ConsolePrefabName = "AlwaysTooLate.Console";

        private GameObject _console;
        private ConsoleHandler _handler;

        private bool _consoleOpen;
        
        protected override void OnAwake()
        {
            // Load and spawn Console canvas
            var consolePrefab = Resources.Load<GameObject>(ConsolePrefabName);
            Debug.Assert(consolePrefab, $"Console prefab ({ConsolePrefabName}) not found!");
            _console = Instantiate(consolePrefab);
            _handler = _console.GetComponentInChildren<ConsoleHandler>();
        }

        protected void Update()
        {
            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                _consoleOpen = !_consoleOpen;

                if (_consoleOpen)
                {
                    _handler.Open();
                }
                else
                {
                    _handler.Close();
                }
            }
        }
    }
}