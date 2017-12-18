using System;
using System.Collections.Generic;
using Unix.Terminal;

namespace Terminal {
    public enum Color
    {
        Black,
        Blue,
        Green,
        Cyan,
        Red,
        Magenta,
        Brown,
        Gray,
        DarkGray,
        BrightBlue,
        BrightGreen,
        BrighCyan,
        BrightRed,
        BrightMagenta,
        BrightYellow,
        White
    }

    public struct Attribute {
        internal int value;
        public Attribute (int v)
        {
            value = v;
        }

        public static implicit operator int (Attribute c) => c.value;
        public static implicit operator Attribute (int v) => new Attribute (v);
    }

    public class ColorScheme {
        public Attribute Normal;
        public Attribute Focus;
        public Attribute HotNormal;
        public Attribute HotFocus;
        public Attribute Marked => HotNormal;
        public Attribute MarkedSelected => HotFocus;
    }

    public static class Colors {
        public static ColorScheme Base, Dialog, Menu, Error;

    }

    public abstract class ConsoleDriver {
        public abstract int Cols {get;}
        public abstract int Rows {get;}
        public abstract void Init ();
        public abstract void Move (int col, int row);
        public abstract void AddCh (int ch);
        public abstract void AddStr (string str);
        public abstract void PrepareToRun ();
        public abstract void Refresh ();
        public abstract void End ();
        public abstract void RedrawTop ();
        public abstract void SetAttribute (Attribute c);

        // Set Colors from limit sets of colors
        public abstract void SetColors (ConsoleColor foreground, ConsoleColor background);

        // Advanced uses - set colors to any pre-set pairs, you would need to init_color
        // that independently with the R, G, B values.
        public abstract void SetColors (short foreColorId, short backgroundColorId);

        public abstract void DrawFrame (Rect region, bool fill);

        Rect clip;
        public Rect Clip {
            get => clip;
            set => this.clip = value;
        }
    }

    public class CursesDriver : ConsoleDriver {
        public override int Cols => Curses.Cols;
        public override int Rows => Curses.Lines;

        // Current row, and current col, tracked by Move/AddCh only
        int ccol, crow;
        bool needMove;
        public override void Move (int col, int row)
        {
            ccol = col;
            crow = row;

            if (Clip.Contains (col, row)) {
                Curses.move (row, col);
                needMove = false;
            } else {
                Curses.move (Clip.Y, Clip.X);
                needMove = true;
            }
        }

        public override void AddCh (int ch)
        {
            if (Clip.Contains (ccol, crow)) {
                if (needMove) {
                    Curses.move (crow, ccol);
                    needMove = false;
                }
                Curses.addch (ch);
            } else
                needMove = true;
            ccol++;
        }

        public override void AddStr (string str)
        {
            // TODO; optimize this to determine if the str fits in the clip region, and if so, use Curses.addstr directly
            foreach (var c in str)
                AddCh ((int) c);
        }

        public override void Refresh() => Curses.refresh ();
        public override void End() => Curses.endwin ();
        public override void RedrawTop() => window.redrawwin ();
        public override void SetAttribute (Attribute c) => Curses.attrset (c.value);
        public Curses.Window window;

        static short last_color_pair = 16;
        static Attribute MakeColor (short f, short b)
        {
            Curses.InitColorPair (++last_color_pair, f, b);
            return new Attribute () { value = Curses.ColorPair (last_color_pair) };
        }

        int [,] colorPairs = new int [16, 16];

        public override void SetColors (ConsoleColor foreground, ConsoleColor background)
        {
            int f = (short) foreground;
            int b = (short)background;
            var v = colorPairs [f, b];
            if ((v & 0x10000) == 0) {
                b = b & 0x7;
                bool bold = (f & 0x8) != 0;
                f = f & 0x7;

                v = MakeColor ((short)f, (short)b) | (bold ? Curses.A_BOLD : 0);
                colorPairs [(int)foreground, (int)background] = v | 0x1000;
            }
            SetAttribute (v & 0xffff);
        }

        Dictionary<int, int> rawPairs = new Dictionary<int, int> ();
        public override void SetColors (short foreColorId, short backgroundColorId)
        {
            int key = (((ushort)foreColorId << 16)) | (ushort) backgroundColorId;
            if (!rawPairs.TryGetValue (key, out var v)) {
                v = MakeColor (foreColorId, backgroundColorId);
                rawPairs [key] = v;
            }
            SetAttribute (v);
        }
        public override void PrepareToRun()
        {
            Curses.timeout (-1);
        }

        public override void DrawFrame (Rect region, bool fill)
        {
            int width = region.Width;
            int height = region.Height;
            int b;

            Move (region.X, region.Y);
            AddCh (Curses.ACS_ULCORNER);
            for (b = 0; b < width - 2; b++)
                AddCh (Curses.ACS_HLINE);
            AddCh (Curses.ACS_URCORNER);
            for (b = 1; b < height - 1; b++) {
                Move (region.X, region.Y + b);
                AddCh (Curses.ACS_VLINE);
                if (fill) {
                    for (int x = 1; x < width - 1; x++)
                        AddCh (' ');
                } else
                    Move (region.X + width - 1, region.Y + b);
                AddCh (Curses.ACS_VLINE);
            }
            Move (region.X, region.Y + height - 1);
            AddCh (Curses.ACS_LLCORNER);
            for (b = 0; b < width - 2; b++)
                AddCh (Curses.ACS_HLINE);
            AddCh (Curses.ACS_LRCORNER);
        }

        public override void Init()
        {
            if (window != null)
                return;

            try {
                window = Curses.initscr ();
            } catch (Exception e){
                Console.WriteLine ("Curses failed to initialize, the exception is: " + e);
            }
            Curses.raw ();
            Curses.noecho ();
            Curses.Window.Standard.keypad (true);
        
            Colors.Base = new ColorScheme ();
            Colors.Dialog = new ColorScheme ();
            Colors.Menu = new ColorScheme ();
            Colors.Error = new ColorScheme ();
            Clip = new Rect (0, 0, Cols, Rows);
            if (Curses.HasColors){
                Curses.StartColor ();
                Curses.UseDefaultColors ();

                Colors.Base.Normal = MakeColor (Curses.COLOR_WHITE, Curses.COLOR_BLUE);
                Colors.Base.Focus = MakeColor (Curses.COLOR_BLACK, Curses.COLOR_CYAN);
                Colors.Base.HotNormal = Curses.A_BOLD | MakeColor (Curses.COLOR_YELLOW, Curses.COLOR_BLUE);
                Colors.Base.HotFocus = Curses.A_BOLD | MakeColor (Curses.COLOR_YELLOW, Curses.COLOR_CYAN);
                Colors.Menu.Normal = Curses.A_BOLD | MakeColor (Curses.COLOR_WHITE, Curses.COLOR_CYAN);
                Colors.Menu.Focus = Curses.A_BOLD | MakeColor (Curses.COLOR_YELLOW, Curses.COLOR_CYAN);
                Colors.Menu.HotNormal = Curses.A_BOLD | MakeColor (Curses.COLOR_WHITE, Curses.COLOR_BLACK);
                Colors.Menu.HotFocus = Curses.A_BOLD | MakeColor (Curses.COLOR_YELLOW, Curses.COLOR_BLACK);
                Colors.Dialog.Normal    = MakeColor (Curses.COLOR_BLACK, Curses.COLOR_WHITE);
                Colors.Dialog.Focus     = MakeColor (Curses.COLOR_BLACK, Curses.COLOR_CYAN);
                Colors.Dialog.HotNormal = MakeColor (Curses.COLOR_BLUE,  Curses.COLOR_WHITE);
                Colors.Dialog.HotFocus  = MakeColor (Curses.COLOR_BLUE,  Curses.COLOR_CYAN);

                Colors.Error.Normal = Curses.A_BOLD | MakeColor (Curses.COLOR_WHITE, Curses.COLOR_RED);
                Colors.Error.Focus = MakeColor (Curses.COLOR_BLACK, Curses.COLOR_WHITE);
                Colors.Error.HotNormal = Curses.A_BOLD | MakeColor (Curses.COLOR_YELLOW, Curses.COLOR_RED);
                Colors.Error.HotFocus = Colors.Error.HotNormal;
            } else {
                Colors.Base.Normal = Curses.A_NORMAL;
                Colors.Base.Focus = Curses.A_REVERSE;
                Colors.Base.HotNormal = Curses.A_BOLD;
                Colors.Base.HotFocus = Curses.A_BOLD | Curses.A_REVERSE;
                Colors.Menu.Normal = Curses.A_REVERSE;
                Colors.Menu.Focus = Curses.A_NORMAL;
                Colors.Menu.HotNormal = Curses.A_BOLD;
                Colors.Menu.HotFocus = Curses.A_NORMAL;
                Colors.Dialog.Normal    = Curses.A_REVERSE;
                Colors.Dialog.Focus     = Curses.A_NORMAL;
                Colors.Dialog.HotNormal = Curses.A_BOLD;
                Colors.Dialog.HotFocus  = Curses.A_NORMAL;
                Colors.Error.Normal = Curses.A_BOLD;
                Colors.Error.Focus = Curses.A_BOLD | Curses.A_REVERSE;
                Colors.Error.HotNormal = Curses.A_BOLD | Curses.A_REVERSE;
                Colors.Error.HotFocus = Curses.A_REVERSE;
            }
        }
    }
}