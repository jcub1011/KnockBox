# KnockBox Plugin SDK

This SDK provides a solution template for developers to create new game plugins for the KnockBox platform.

## Prerequisites
- .NET 10.0 SDK
- Access to the `KnockBox.Core` NuGet package.

## Installation
To install the project template, run:
```bash
dotnet new install KnockBox.Plugin.Templates
```

## Creating a New Plugin
Run the following command, replacing `MyGame` with your game's name:
```bash
dotnet new knockbox-plugin -n MyGame
```

This will create a new directory `MyGame` with:
- `src/MyGame/`: The game plugin logic and UI.
- `tests/MyGame.Tests/`: Unit tests for your game engine.
- `MyGame.slnx`: A modern solution file to open in your IDE.

## Building Your Plugin
Navigate to the `MyGame` directory and run:
```bash
dotnet build
```

## Project Structure
- `PluginModule.cs`: Metadata and service registration for your plugin.
- `Services/Logic/GameEngine.cs`: Core logic for game phases and state transitions.
- `Services/State/GameState.cs`: Data model for the active game session.
- `Pages/GameLobby.razor`: The main entry point for players in a room.
- `Components/GameTile.razor`: The visual tile shown on the KnockBox home screen.
