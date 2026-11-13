using DocumentFinder.Core;

namespace DocumentFinder.App;

internal sealed class UpdateForm:Form
{
    private readonly GitHubReleaseClient client=new();
    private readonly Label status=new(){Dock=DockStyle.Fill,Text="새 버전을 확인하고 있습니다…",AutoEllipsis=true};
    private readonly RichTextBox notes=new(){Dock=DockStyle.Fill,ReadOnly=true,BackColor=SystemColors.Window,BorderStyle=BorderStyle.FixedSingle,DetectUrls=false,AccessibleName="변경 사항"};
    private readonly ProgressBar progress=new(){Dock=DockStyle.Fill};
    private readonly Button install=new(){Text="다운로드 후 설치",AutoSize=true,Enabled=false};
    private readonly Button check=new(){Text="다시 확인",AutoSize=true,Enabled=false};
    private readonly Button close=new(){Text="닫기",AutoSize=true};
    private CancellationTokenSource? operation;
    private AppRelease? release;
    public string? InstallerPath{get;private set;}
    public UpdateForm(AppRelease? known=null)
    {
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        release=known;Text="모두찾아줘 업데이트";Icon=AppBrand.LoadIcon();Size=new(620,520);MinimumSize=new(530,440);StartPosition=FormStartPosition.CenterParent;
        Font=new Font("맑은 고딕",10);AutoScaleMode=AutoScaleMode.Dpi;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),RowCount=6,ColumnCount=1};
        foreach(var h in new[]{32,56})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,27));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,39));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
        layout.Controls.Add(new Label{Text="현재 버전 "+AppBrand.VersionText,AutoSize=true,Dock=DockStyle.Fill},0,0);
        layout.Controls.Add(status,0,1);layout.Controls.Add(notes,0,2);layout.Controls.Add(progress,0,3);
        layout.Controls.Add(new Label{Text="GitHub에서 설치 파일을 받고 SHA-256을 확인한 뒤 설치합니다.",Dock=DockStyle.Fill,AutoEllipsis=true},0,4);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};buttons.Controls.AddRange([close,install,check]);layout.Controls.Add(buttons,0,5);Controls.Add(layout);
        close.Click+=(_,_)=>{if(operation is not null){operation.Cancel();status.Text="취소하고 있습니다…";}else Close();};
        check.Click+=async(_,_)=>await Check();install.Click+=async(_,_)=>await Download();
        Shown+=async(_,_)=>{if(release is not null)ShowRelease();else await Check();};
        FormClosing+=(_,e)=>{if(operation is not null){operation.Cancel();e.Cancel=true;status.Text="취소하고 있습니다…";}};
        FormClosed+=(_,_)=>client.Dispose();
    }
    private void ShowRelease()
    {
        status.Text=release is null?"최신 버전을 사용하고 있습니다.":$"새 버전 {release.Version}을 설치할 수 있습니다.\n설치 후 프로그램이 다시 열리고 기존 검색 데이터는 유지됩니다.";
        notes.Text=release?.Notes??"새 버전이 공개되면 이곳에서 설치할 수 있습니다.";install.Enabled=release is not null;check.Enabled=true;
    }
    private async Task Check()
    {
        check.Enabled=install.Enabled=false;operation=new();status.Text="GitHub에서 최신 정식 버전을 확인하고 있습니다…";
        try{release=await client.CheckAsync(AppBrand.Version,operation.Token);ShowRelease();}
        catch(OperationCanceledException){status.Text="확인을 취소했습니다. 나중에 다시 확인할 수 있습니다.";}
        catch(Exception ex){status.Text="업데이트를 확인하지 못했습니다.";notes.Text=ex.Message;}
        finally{operation.Dispose();operation=null;check.Enabled=true;}
    }
    private async Task Download()
    {
        if(release is null)return;check.Enabled=install.Enabled=false;close.Text="다운로드 취소";operation=new();
        status.Text=$"{release.Version} 다운로드 중…";
        try
        {
            InstallerPath=await client.DownloadAsync(release,Path.Combine(Program.DataDirectory,"updates"),new Progress<int>(n=>{if(!IsDisposed){progress.Value=n;status.Text=$"{release.Version} 다운로드 중 · {n}%";}}),operation.Token);
            status.Text="검증 완료. 설치를 시작합니다.";operation.Dispose();operation=null;DialogResult=DialogResult.OK;Close();
        }
        catch(OperationCanceledException){status.Text="다운로드를 취소했습니다. 현재 버전은 그대로 사용할 수 있습니다.";}
        catch(Exception ex){status.Text="설치 파일을 받지 못했습니다.";notes.Text=ex.Message;}
        finally{operation?.Dispose();operation=null;if(!IsDisposed){check.Enabled=true;install.Enabled=release is not null;close.Text="닫기";}}
    }
}
