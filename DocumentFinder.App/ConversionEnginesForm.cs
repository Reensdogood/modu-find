using DocumentFinder.Core;

namespace DocumentFinder.App;

internal sealed class ConversionEnginesForm:Form
{
    private readonly FileeConversionClient client;
    private readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
    private readonly Label status=new(){Dock=DockStyle.Fill,Text="추가 엔진 상태를 확인하고 있습니다…"};
    private readonly ProgressBar progress=new(){Dock=DockStyle.Fill};
    private readonly Button install=new(){Text="선택 엔진 설치",AutoSize=true,Enabled=false},close=new(){Text="닫기",AutoSize=true};
    private CancellationTokenSource? cancel;
    private bool closeWhenDone;
    public ConversionEnginesForm(FileeConversionClient client)
    {
        this.client=client;AutoScaleDimensions=new(96,96);AutoScaleMode=AutoScaleMode.Dpi;Font=new("맑은 고딕",10);Text="문서 변환 · 추가 엔진";Icon=AppBrand.LoadIcon();Size=new(780,480);MinimumSize=new(650,420);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=5};layout.RowStyles.Add(new(SizeType.Absolute,58));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,40));layout.RowStyles.Add(new(SizeType.Absolute,22));layout.RowStyles.Add(new(SizeType.Absolute,42));
        layout.Controls.Add(new Label{Text="선택한 엔진만 인터넷에서 내려받습니다. 문서는 전송하지 않습니다.\n설치 후에는 오프라인에서도 사용할 수 있습니다. 각 엔진의 원본 라이선스를 따릅니다.",Dock=DockStyle.Fill},0,0);
        foreach(var (name,width) in new[]{("엔진",42f),("버전",15f),("상태",23f),("다운로드",20f)})grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=name,FillWeight=width});grid.RowTemplate.Height=38;layout.Controls.Add(grid,0,1);layout.Controls.Add(status,0,2);layout.Controls.Add(progress,0,3);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};buttons.Controls.AddRange([close,install]);layout.Controls.Add(buttons,0,4);Controls.Add(layout);
        grid.SelectionChanged+=(_,_)=>UpdateSelection();Shown+=async(_,_)=>await Reload();
        install.Click+=async(_,_)=>await Install();close.Click+=(_,_)=>{if(cancel is null)Close();else{cancel.Cancel();status.Text="설치를 중지하고 있습니다…";}};
        FormClosing+=(_,e)=>{if(cancel is not null){cancel.Cancel();closeWhenDone=true;e.Cancel=true;}};
    }
    private async Task Reload()
    {
        try
        {
            var engines=await client.EnginesAsync();grid.Rows.Clear();
            foreach(var e in engines){var n=grid.Rows.Add(e.Name,e.Version,e.Status is "installed"?"설치됨":e.Status is "owncopy"?"PC에 설치됨":"추가 설치 필요",e.DownloadSize>0?$"{e.DownloadSize/1024d/1024:N0} MB":"—");grid.Rows[n].Tag=e;}
            status.Text="구형 DOC·PPT·ODT는 LibreOffice, 일부 마크업은 Pandoc이 필요합니다.";UpdateSelection();
        }
        catch(Exception ex){status.Text="추가 엔진을 확인하지 못했습니다. "+ex.Message;}
    }
    private void UpdateSelection()=>install.Enabled=cancel is null&&grid.CurrentRow?.Tag is ConversionEngine e&&e.Downloadable&&e.Status=="notinstalled";
    private async Task Install()
    {
        if(cancel is not null||grid.CurrentRow?.Tag is not ConversionEngine engine)return;
        cancel=new();install.Enabled=false;grid.Enabled=false;close.Text="설치 중지";
        try{await client.InstallEngineAsync(engine.Id,new Progress<ConversionProgress>(p=>{if(IsDisposed||cancel is null)return;progress.Value=p.Percent;status.Text="추가 엔진 설치 중 · "+p.Percent+"% "+p.Message;}),cancel.Token);status.Text="추가 엔진을 설치했습니다.";}
        catch(OperationCanceledException){status.Text="추가 엔진 설치를 중지했습니다.";}
        catch(Exception ex){status.Text="설치하지 못했습니다. "+ex.Message;}
        finally{cancel.Dispose();cancel=null;grid.Enabled=true;close.Text="닫기";if(closeWhenDone)Close();else await Reload();}
    }
}
