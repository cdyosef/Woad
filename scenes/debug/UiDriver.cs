using Godot;

namespace Woad.Scenes.Debug;

/// <summary>
/// Synthetic input for the debug UI tests: real mouse and keyboard events pushed through the
/// viewport, so they exercise the same hit-testing, focus and signals as a player would.
/// </summary>
public static class UiDriver
{
    public static async Task NextFrame(Node context) =>
        await context.ToSignal(context.GetTree(), SceneTree.SignalName.ProcessFrame);

    /// <summary>Waits a frame at a time until the condition holds, failing after <paramref name="maxFrames"/>.</summary>
    public static async Task WaitFor(Node context, Func<bool> condition, string what, int maxFrames = 60000)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            if (condition())
            {
                return;
            }

            await NextFrame(context);
        }

        throw new InvalidOperationException($"Timed out waiting for {what}.");
    }

    public static Task Click(Node context, Control control) => ClickAt(context, control.GetGlobalRect().GetCenter());

    /// <summary>Moves the mouse to a point in 640x360 canvas coordinates and clicks it.</summary>
    public static async Task ClickAt(Node context, Vector2 position)
    {
        Viewport viewport = context.GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, inLocalCoords: true);
        foreach (bool pressed in new[] { true, false })
        {
            viewport.PushInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = pressed,
                Position = position,
                GlobalPosition = position,
            }, inLocalCoords: true);
            await NextFrame(context);
        }
    }

    /// <summary>Holds the right mouse button at <paramref name="from"/>, drags to <paramref name="to"/> in a few steps and lets go.</summary>
    public static async Task RightDrag(Node context, Vector2 from, Vector2 to)
    {
        Viewport viewport = context.GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = from, GlobalPosition = from }, inLocalCoords: true);
        viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = from, GlobalPosition = from }, inLocalCoords: true);
        await NextFrame(context);
        Vector2 at = from;
        for (int step = 1; step <= 8; step++)
        {
            Vector2 next = from.Lerp(to, step / 8f);
            viewport.PushInput(new InputEventMouseMotion { Position = next, GlobalPosition = next, Relative = next - at, ButtonMask = MouseButtonMask.Right }, inLocalCoords: true);
            at = next;
            await NextFrame(context);
        }

        viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = to, GlobalPosition = to }, inLocalCoords: true);
        await NextFrame(context);
    }

    /// <summary>Types text into whichever control has keyboard focus.</summary>
    public static async Task Type(Node context, string text)
    {
        Viewport viewport = context.GetViewport();
        foreach (char character in text)
        {
            foreach (bool pressed in new[] { true, false })
            {
                viewport.PushInput(new InputEventKey { Pressed = pressed, Unicode = character }, inLocalCoords: true);
            }

            await NextFrame(context);
        }
    }

    public static void Check(bool condition, string expectation)
    {
        if (!condition)
        {
            throw new InvalidOperationException(expectation);
        }

        GD.Print($"  ok: {expectation}");
    }
}
