using System;
using System.Drawing;

#nullable enable
namespace FishingFun
{
    /// <summary>
    /// Checks that the cursor is put on the float once per cast, the way a
    /// person puts it there and leaves it, and that it is not moved while the
    /// user is holding a mouse button down.
    /// </summary>
    public static class CursorBehaviourRegression
    {
        private static int failures;
        private static int checks;

        private static void Check(string what, bool passed, string detail = "")
        {
            checks++;
            Console.WriteLine((passed ? "PASS " : "FAIL ") + what + (detail.Length > 0 ? "  (" + detail + ")" : ""));
            if (!passed) failures++;
        }

        public static int Main()
        {
            Console.WriteLine("=== the cursor is put on the float once ===");
            {
                var cursor = new FloatCursor();
                var floatPosition = new Point(1000, 500);

                Check("the cursor is put on the float when it is first seen",
                    cursor.Place(floatPosition, false));

                Check("the cursor is not put on the float again",
                    !cursor.Place(floatPosition, false));

                Check("the cursor is on the float",
                    cursor.Placed);

                Check("the place the cursor was put on is remembered",
                    cursor.PlacedOn == floatPosition);
            }

            Console.WriteLine();
            Console.WriteLine("=== the float drifting does not move the cursor ===");
            {
                var cursor = new FloatCursor();
                cursor.Place(new Point(1000, 500), false);

                Check("the cursor is not moved when the float drifts",
                    !cursor.Place(new Point(1000, 507), false));

                Check("the cursor is still where it was",
                    cursor.PlacedOn == new Point(1000, 500));
            }

            Console.WriteLine();
            Console.WriteLine("=== the cursor is not moved while the user holds a button ===");
            {
                var cursor = new FloatCursor();

                Check("the cursor is not put on the float while the user holds a button",
                    !cursor.Place(new Point(1000, 500), true));

                Check("the float is not marked as placed",
                    !cursor.Placed);

                Check("the cursor is put on the float once the user lets go",
                    cursor.Place(new Point(1000, 500), false));
            }

            Console.WriteLine();
            Console.WriteLine("=== every cast gets its own cursor ===");
            {
                var first = new FloatCursor();
                var second = new FloatCursor();

                first.Place(new Point(1000, 500), false);

                Check("a new cast places the cursor again",
                    second.Place(new Point(1100, 600), false));

                Check("the two casts are separate",
                    first.PlacedOn != second.PlacedOn);
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)");
            return failures == 0 ? 0 : 1;
        }
    }
}
