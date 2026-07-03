using System.Drawing;

namespace Russkyc.Fene;

public class Display
{
    public Rectangle Bounds { get; set; }
    public Rectangle WorkingArea { get; set; }
    public bool IsPrimary { get; set; }
    
    public int X => Bounds.X;
    public int Y => Bounds.Y;
    public int Width => Bounds.Width;
    public int Height => Bounds.Height;
}