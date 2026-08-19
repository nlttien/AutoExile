using System;
using System.Threading;
using System.Windows.Forms;
using ExileCore;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace AutoExile.ShopBuyer.Utils
{
    public static class MouseHelper
    {
        private static readonly Random Rnd = new();

        public static void MoveMouse(Vector2 target)
        {
            Input.SetCursorPos(target);
        }

        public static void MoveMouseWithJitter(SharpDX.RectangleF rect, float offsetY = 6f)
        {
            var centerX = rect.Center.X;
            var centerY = rect.Center.Y + offsetY;

            var jitterX = (float)(Rnd.NextDouble() * 3.0 - 1.5);
            var jitterY = (float)(Rnd.NextDouble() * 3.0 - 1.5);

            Input.SetCursorPos(new Vector2(centerX + jitterX, centerY + jitterY));
        }

        public static void CtrlLeftClickAt(Vector2 target, int hoverWaitMs = 130, int holdMs = 50)
        {
            Input.SetCursorPos(target);
            Thread.Sleep(hoverWaitMs);
            Input.KeyDown(Keys.LControlKey);
            Thread.Sleep(30);
            Input.LeftDown();
            Thread.Sleep(holdMs);
            Input.LeftUp();
            Thread.Sleep(30);
            Input.KeyUp(Keys.LControlKey);
            Thread.Sleep(30);
        }

        public static void CtrlLeftClick()
        {
            Input.KeyDown(Keys.LControlKey);
            Thread.Sleep(35);
            Input.LeftDown();
            Thread.Sleep(50);
            Input.LeftUp();
            Thread.Sleep(35);
            Input.KeyUp(Keys.LControlKey);
        }

        public static void LeftClick()
        {
            Input.LeftDown();
            Thread.Sleep(40);
            Input.LeftUp();
        }

        public static void LeftClickAt(Vector2 target, int hoverWaitMs = 100, int holdMs = 45)
        {
            Input.SetCursorPos(target);
            Thread.Sleep(hoverWaitMs);
            Input.LeftDown();
            Thread.Sleep(holdMs);
            Input.LeftUp();
            Thread.Sleep(30);
        }

        public static int GetRandomDelay(int minMs, int maxMs)
        {
            if (minMs >= maxMs) return minMs;
            return Rnd.Next(minMs, maxMs + 1);
        }
    }
}
