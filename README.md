# AlwaysTooLate.Console

AlwaysTooLate Console module, provides a plug'n'play runtime console solution, that can be used for reading logs, using developer commands (aka 'cheats') and many other things.

# Installation

Before installing this module, be sure to have installed these:

- [AlwaysTooLate.Core](https://github.com/AlwaysTooLate/AlwaysTooLate.Core)
- [AlwaysTooLate.Commands](https://github.com/AlwaysTooLate/AlwaysTooLate.Commands)
- [AlwaysTooLate.CVars](https://github.com/AlwaysTooLate/AlwaysTooLate.CVars)

Open your target project in Unity and use the Unity Package Manager (`Window` -> `Package Manager` -> `+` -> `Add package from git URL`) and paste the following URL:
https://github.com/AlwaysTooLate/AlwaysTooLate.Console.git

# Setup

After succesfull installation, open a scene that is loaded first when starting your game (we recommend having an entry scene called Main that is only used for initializing core systems and utilities, which then loads the next scene, that is supposed to start the game - like a Main Menu). In the module's directory, go to the Resources folder and place the `AlwaysTooLate.Console.prefab` from it into the scene. In the ConsoleManager component, you can change the OpenConsoleKey (its default value is BackQuote) and attach custom Unity Events, that will be invoked when Showing and/or Hiding the console.

# Basic Usage

To open the console, press the OpenConsoleKey (BackQuote by default). The console will appear and you will be able to type in commands, read logs, etc.

# Contribution

We do accept PR. Feel free to contribute. If you want to find us, look for the ATL staff on the official [AlwaysTooLate Discord Server](https://discord.alwaystoolate.com/)

*AlwaysTooLate.Console (c) 2018-2020 Always Too Late.*
