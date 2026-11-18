using System.Text;
using DocumentFinder.Core;

namespace DocumentFinder.App;

internal static class DocumentTools
{
    public static void Export(IWin32Window owner,SearchHit hit)
    {
        using var dialog=new SaveFileDialog{Title="문서 본문을 Markdown으로 저장",Filter="Markdown|*.md",FileName=Path.GetFileNameWithoutExtension(hit.Name)+".md"};
        if(dialog.ShowDialog(owner)!=DialogResult.OK)return;
        if(Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(hit.Path),StringComparison.OrdinalIgnoreCase)){MessageBox.Show(owner,"원본과 다른 저장 경로를 선택하세요.");return;}
        File.WriteAllText(dialog.FileName,"# "+hit.Name+"\n\n원본: "+hit.Path+"\n색인 상태: "+hit.Status+"\n\n"+hit.Content,new UTF8Encoding(false));
    }
    public static string? EditTags(IWin32Window owner,string current)
    {
        using var form=new Form{Text="문서 태그",Size=new(520,210),MinimumSize=new(480,200),StartPosition=FormStartPosition.CenterParent,Font=new("맑은 고딕",10),Icon=AppBrand.LoadIcon()};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=3};layout.RowStyles.Add(new(SizeType.Absolute,35));layout.RowStyles.Add(new(SizeType.Absolute,40));layout.RowStyles.Add(new(SizeType.Percent,100));
        layout.Controls.Add(new Label{Text="쉼표로 구분합니다. 검색창에 #태그로 찾을 수 있습니다.",Dock=DockStyle.Fill},0,0);var input=new TextBox{Text=current,Dock=DockStyle.Fill,MaxLength=1000};layout.Controls.Add(input,0,1);
        var save=new Button{Text="태그 저장",AutoSize=true,Dock=DockStyle.Right,DialogResult=DialogResult.OK};layout.Controls.Add(save,0,2);form.Controls.Add(layout);form.AcceptButton=save;
        return form.ShowDialog(owner)==DialogResult.OK?input.Text:null;
    }
    public static void ShowText(IWin32Window owner,string title,string text)
    {
        using var form=new Form{Text=title,Size=new(1000,780),MinimumSize=new(750,550),StartPosition=FormStartPosition.CenterParent,Font=new("맑은 고딕",10),Icon=AppBrand.LoadIcon()};
        var content=new RichTextBox{Dock=DockStyle.Fill,ReadOnly=true,Text=text,DetectUrls=false,BackColor=Color.White};form.Controls.Add(content);form.ShowDialog(owner);
    }
}
