namespace DoomSharp;

public static class BrowserInput
{
    public static Buttons FromCode(string code) => code switch
    {
        "KeyW" or "ArrowUp" => Buttons.Forward, "KeyS" or "ArrowDown" => Buttons.Back,
        "KeyA" => Buttons.Left, "KeyD" => Buttons.Right,
        "ArrowLeft" => Buttons.TurnLeft, "ArrowRight" => Buttons.TurnRight,
        "KeyE" or "Space" => Buttons.Use, "ShiftLeft" or "ShiftRight" => Buttons.Run,
        "ControlLeft" or "ControlRight" or "Mouse0" => Buttons.Fire,
        "Digit1" => Buttons.Pistol, "Digit2" => Buttons.Shotgun,
        "Digit4" => Buttons.Chaingun, "Digit5" => Buttons.RocketLauncher,
        "Tab" => Buttons.Map, "KeyR" => Buttons.Restart, "F1" => Buttons.Menu,
        "F5" => Buttons.Save, "F9" => Buttons.Load, _ => Buttons.None
    };
}
