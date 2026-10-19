using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using djack.RogueSurvivor.Engine;

static class InputReaderTests
{
    sealed class FakeSource : IPlayerInputSource
    {
        public readonly Queue<KeyEventArgs> Keys = new Queue<KeyEventArgs>();
        public readonly Queue<Point> Positions = new Queue<Point>();
        public readonly Queue<MouseButtons?> Buttons = new Queue<MouseButtons?>();
        public int KeyReads;
        public KeyEventArgs PeekKey()
        {
            KeyReads++;
            return Keys.Count == 0 ? null : Keys.Dequeue();
        }
        public Point MousePosition() { return Positions.Dequeue(); }
        public MouseButtons? PeekMouseButtons() { return Buttons.Count == 0 ? null : Buttons.Dequeue(); }
    }

    public static void Run()
    {
        FakeSource pending = new FakeSource();
        pending.Positions.Enqueue(new Point(4, 4));
        pending.Positions.Enqueue(new Point(5, 4));
        PlayerInputEvent key = new PlayerInputReader(pending).Read(new KeyEventArgs(Keys.M));
        Check.Equal(Keys.M, key.Key.KeyCode, "pending key delivered");
        Check.Equal(new Point(5, 4), key.MousePosition, "pending key retains current mouse target");
        Check.Equal(null, key.MouseButtons, "keyboard input does not invent a click");
        Check.Equal(0, pending.KeyReads, "pending key is not discarded");

        FakeSource give = new FakeSource();
        give.Keys.Enqueue(null);
        give.Keys.Enqueue(new KeyEventArgs(Keys.G));
        give.Positions.Enqueue(new Point(4, 4));
        give.Positions.Enqueue(new Point(8, 9));
        give.Buttons.Enqueue(MouseButtons.Left);
        PlayerInputEvent gift = new PlayerInputReader(give).Read(null);
        Check.Equal(Keys.G, gift.Key.KeyCode, "give key delivered");
        Check.Equal(new Point(8, 9), gift.MousePosition, "give uses current hover even after mouse moves");
        Check.Equal(null, gift.MouseButtons, "key takes priority over click");
        Check.Equal(1, give.Buttons.Count, "key leaves mouse button input untouched");

        FakeSource move = new FakeSource();
        move.Positions.Enqueue(new Point(4, 4));
        move.Positions.Enqueue(new Point(5, 4));
        PlayerInputEvent hover = new PlayerInputReader(move).Read(null);
        Check.Equal(new Point(5, 4), hover.MousePosition, "mouse hover delivered");
        Check.Equal(null, hover.Key, "hover has no key");

        FakeSource click = new FakeSource();
        click.Positions.Enqueue(new Point(4, 4));
        click.Positions.Enqueue(new Point(4, 4));
        click.Buttons.Enqueue(MouseButtons.Left);
        PlayerInputEvent pressed = new PlayerInputReader(click).Read(null);
        Check.Equal(MouseButtons.Left, pressed.MouseButtons, "click delivered without movement");

        FakeSource repeated = new FakeSource();
        repeated.Keys.Enqueue(new KeyEventArgs(Keys.M));
        repeated.Keys.Enqueue(new KeyEventArgs(Keys.NumPad6));
        repeated.Positions.Enqueue(new Point(4, 4));
        repeated.Positions.Enqueue(new Point(4, 4));
        PlayerInputEvent next = new PlayerInputReader(repeated).Read(null);
        Check.Equal(Keys.NumPad6, next.Key.KeyCode,
            "modal input drops one repeated key and accepts the next one");
    }
}
