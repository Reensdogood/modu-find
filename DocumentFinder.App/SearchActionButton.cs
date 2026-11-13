namespace DocumentFinder.App;

/// <summary>Draws its caption explicitly so native theme/DPI rendering cannot hide it.</summary>
internal sealed class SearchActionButton:Button
{
    private bool hovered,pressed;
    public SearchActionButton()
    {
        Text="검색";AccessibleName="검색";AutoSize=false;MinimumSize=new Size(100,40);
        FlatStyle=FlatStyle.Flat;UseVisualStyleBackColor=false;
        BackColor=Color.FromArgb(29,78,216);ForeColor=Color.White;
        Font=new Font("맑은 고딕",10.5F,FontStyle.Bold);
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var highContrast=SystemInformation.HighContrast;
        var background=!Enabled?SystemColors.Control:highContrast?SystemColors.Highlight:
            pressed?Color.FromArgb(23,55,151):hovered?Color.FromArgb(30,64,175):BackColor;
        var foreground=!Enabled?SystemColors.GrayText:highContrast?SystemColors.HighlightText:ForeColor;
        e.Graphics.Clear(background);
        using var border=new Pen(highContrast?SystemColors.WindowText:Color.FromArgb(23,55,151));
        e.Graphics.DrawRectangle(border,0,0,Math.Max(0,Width-1),Math.Max(0,Height-1));
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,foreground,
            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),foreground,background);
    }
    protected override void OnMouseEnter(EventArgs e){hovered=true;base.OnMouseEnter(e);Invalidate();}
    protected override void OnMouseLeave(EventArgs e){hovered=pressed=false;base.OnMouseLeave(e);Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left)pressed=true;base.OnMouseDown(e);Invalidate();}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;base.OnMouseUp(e);Invalidate();}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
}
