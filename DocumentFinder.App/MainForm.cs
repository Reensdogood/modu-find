using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DocumentFinder.Core;

namespace DocumentFinder.App;

internal sealed class MainForm : Form
{
    private readonly SearchStore store;
    private readonly TextBox query=new(){PlaceholderText="관악 FP처럼 기억나는 단어를 입력하세요",Dock=DockStyle.Fill,AccessibleName="검색어"};
    private readonly ComboBox kind=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=120,AccessibleName="문서 유형"};
    private readonly ComboBox order=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=122,AccessibleName="정렬"};
    private readonly CheckBox namesOnly=new(){Text="파일명·경로만",AutoSize=true,AccessibleName="파일명과 경로만 검색"};
    private readonly ListBox roots=new(){Dock=DockStyle.Fill,HorizontalScrollbar=true,IntegralHeight=false,AccessibleName="색인할 위치"};
    private readonly CheckBox online=new(){Text="클라우드 파일 내용도 읽기",AutoSize=true,Checked=true};
    private readonly Button start=Button("색인 시작"),stop=Button("중지"),open=Button("파일 열기"),folder=Button("폴더 열기"),copy=Button("경로 복사");
    private readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,
        AllowUserToResizeRows=false,AutoGenerateColumns=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,AccessibleName="검색 결과"};
    private readonly Label count=Label("검색어를 입력하거나 색인을 시작하세요."),statistics=Label(""),progressText=Label("준비됨"),current=Label(""),detailTitle=Label("문서를 선택하세요."),detailInfo=Label(""),detailStatus=Label("");
    private readonly TextBox detailPath=new(){ReadOnly=true,Multiline=true,BorderStyle=BorderStyle.None,Dock=DockStyle.Fill,BackColor=Color.White,AccessibleName="선택한 파일 경로"};
    private readonly RichTextBox preview=new(){Dock=DockStyle.Fill,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Color.White,DetectUrls=false,AccessibleName="일치 내용 미리보기"};
    private readonly ListBox sheets=new(){Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,HorizontalScrollbar=true,AccessibleName="엑셀 시트 목록"};
    private readonly ProgressBar bar=new(){Dock=DockStyle.Fill,Height=8,AccessibleName="색인 진행률"};
    private readonly CheckBox wholeText=new(){Text="본문 전체 보기",AutoSize=true};
    private readonly Button update=Button("업데이트 확인");
    private readonly Button documentConvert=Button("문서 변환");
    private readonly Button research=Button("근거 검색");
    private readonly CheckBox bookmarks=new(){Text="북마크",AutoSize=true},watchChanges=new(){Text="변경 자동 반영",AutoSize=true};
    private FolderWatchService? watcher;
    private SearchHit? comparison;
    private AppRelease? availableRelease;
    private readonly System.Windows.Forms.Timer debounce=new(){Interval=280};
    private readonly System.Windows.Forms.Timer duringScan=new(){Interval=2500};
    private readonly ToolTip tips=new(){AutoPopDelay=15000};
    private readonly SplitContainer resultSplit=new(){Size=new Size(1000,500),Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel2,SplitterWidth=8};
    private List<SearchHit> hits=[];
    private SearchHit? selected;
    private CancellationTokenSource? scanCts;
    private Task? scanTask;
    private int searchVersion;
    private bool closing;
    private DateTime lastProgress=DateTime.MinValue;
    private readonly Color ink=Color.FromArgb(27,36,51),muted=Color.FromArgb(82,95,114),accent=Color.FromArgb(29,78,216);

    public MainForm(SearchStore store)
    {
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        this.store=store;Text="모두찾아줘";Icon=AppBrand.LoadIcon();Size=new Size(1380,880);MinimumSize=new Size(1060,680);StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("맑은 고딕",10F);ForeColor=ink;BackColor=Color.White;AutoScaleMode=AutoScaleMode.Dpi;
        Build();LoadSettings();WireEvents();
        watcher=new(store,()=>scanTask is {IsCompleted:false},message=>{if(!IsDisposed)progressText.Text=message;});
        watchChanges.Checked=store.GetSetting("watchChanges")=="true";
        watchChanges.CheckedChanged+=(_,_)=>{store.SetSetting("watchChanges",watchChanges.Checked?"true":"false");watcher.Configure(roots.Items.Cast<string>(),watchChanges.Checked);};
        watcher.Configure(roots.Items.Cast<string>(),watchChanges.Checked);
        if(SystemInformation.HighContrast)UseSystemColors(this);
        Shown+=async(_,_)=>{resultSplit.SplitterDistance=Math.Max(350,resultSplit.Width-365);await Search();query.Focus();_ = CheckUpdatesInBackground();};
    }
    private static Button Button(string text)=>new(){Text=text,AutoSize=true,Height=36,MinimumSize=new Size(75,34),FlatStyle=FlatStyle.System,Margin=new Padding(0,0,6,0)};
    private static Label Label(string text)=>new(){Text=text,AutoSize=false,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
    private static FlowLayoutPanel Flow()=>new(){Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true,Padding=Padding.Empty,Margin=Padding.Empty};

    private static void UseSystemColors(Control parent)
    {
        parent.ForeColor=SystemColors.WindowText;parent.BackColor=SystemColors.Window;
        if(parent is Button button){button.ForeColor=SystemColors.ControlText;button.BackColor=SystemColors.Control;}
        if(parent is LinkLabel link)link.LinkColor=SystemColors.HotTrack;
        if(parent is DataGridView view)
        {
            view.BackgroundColor=SystemColors.Window;view.GridColor=SystemColors.ControlDark;
            view.DefaultCellStyle.BackColor=SystemColors.Window;view.DefaultCellStyle.ForeColor=SystemColors.WindowText;
            view.DefaultCellStyle.SelectionBackColor=SystemColors.Highlight;view.DefaultCellStyle.SelectionForeColor=SystemColors.HighlightText;
            view.ColumnHeadersDefaultCellStyle.BackColor=SystemColors.Control;view.ColumnHeadersDefaultCellStyle.ForeColor=SystemColors.ControlText;
        }
        foreach(Control child in parent.Controls)UseSystemColors(child);
    }

    private void Build()
    {
        var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=Padding.Empty};
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,88));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,160));Controls.Add(shell);
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=6,RowCount=1,Margin=Padding.Empty,Padding=new Padding(22,12,20,12),BackColor=Color.FromArgb(246,248,251)};
        header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,48));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,245));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,142));
        header.Controls.Add(new PictureBox{Image=Icon!.ToBitmap(),SizeMode=PictureBoxSizeMode.Zoom,Dock=DockStyle.Fill,Margin=new Padding(0,4,10,4),AccessibleName="모두찾아줘 아이콘"},0,0);
        var name=Label("모두찾아줘");name.Font=new Font(Font.FontFamily,18,FontStyle.Bold);header.Controls.Add(name,1,0);
        var desc=Label("파일명 · 엑셀 시트명 · 문서 본문 검색");desc.ForeColor=muted;desc.AutoEllipsis=true;header.Controls.Add(desc,2,0);
        research.Dock=DockStyle.Fill;research.Margin=new Padding(0,4,8,4);research.Click+=(_,_)=>{using var dialog=new ResearchForm(store,query.Text);dialog.ShowDialog(this);};header.Controls.Add(research,3,0);
        documentConvert.Dock=DockStyle.Fill;documentConvert.Margin=new Padding(0,4,8,4);documentConvert.Click+=(_,_)=>OpenConversion();header.Controls.Add(documentConvert,4,0);
        update.Dock=DockStyle.Fill;update.Margin=new Padding(0,4,0,4);update.Click+=async(_,_)=>await OpenUpdates();header.Controls.Add(update,5,0);shell.Controls.Add(header,0,0);

        var work=new SplitContainer{Size=new Size(1300,600),Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1,SplitterWidth=1,SplitterDistance=248};
        work.Panel1MinSize=220;work.Panel2MinSize=600;work.Panel1.BackColor=Color.FromArgb(246,248,251);shell.Controls.Add(work,0,1);
        var side=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=10,Padding=new Padding(18,18,14,14)};
        foreach(var h in new float[]{30,38,38})side.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        side.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        foreach(var h in new float[]{38,45,44,52,38,32})side.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        var sideTitle=Label("색인할 위치");sideTitle.Font=new Font(Font,FontStyle.Bold);side.Controls.Add(sideTitle,0,0);
        var add=Button("폴더 추가");add.Dock=DockStyle.Fill;add.Click+=(_,_)=>AddFolder();side.Controls.Add(add,0,1);
        var pc=Button("PC 전체 선택");pc.Dock=DockStyle.Fill;pc.Click+=(_,_)=>{SetRoots(DriveInfo.GetDrives().Where(d=>d.IsReady).Select(d=>d.RootDirectory.FullName));SaveRoots();};side.Controls.Add(pc,0,2);
        roots.BackColor=Color.White;roots.BorderStyle=BorderStyle.FixedSingle;side.Controls.Add(roots,0,3);
        var remove=Button("선택 위치 제거");remove.Dock=DockStyle.Fill;remove.Click+=(_,_)=>{if(roots.SelectedIndex>=0){roots.Items.RemoveAt(roots.SelectedIndex);SaveRoots();}};side.Controls.Add(remove,0,4);
        var hint=Label("위치 제거는 기존 색인을 지우지 않습니다.");hint.Font=new Font(Font.FontFamily,8.5F);hint.ForeColor=muted;side.Controls.Add(hint,0,5);
        online.Dock=DockStyle.Fill;side.Controls.Add(online,0,6);
        var cloudHint=Label("온라인 전용 파일은 내용을 읽을 때 다운로드될 수 있습니다.");cloudHint.Font=new Font(Font.FontFamily,8.5F);cloudHint.ForeColor=muted;side.Controls.Add(cloudHint,0,7);
        var scanButtons=Flow();start.Width=115;stop.Width=65;stop.Enabled=false;scanButtons.Controls.AddRange([start,stop]);side.Controls.Add(scanButtons,0,8);
        var clean=new LinkLabel{Text="없는 파일 색인 정리",Dock=DockStyle.Fill,LinkColor=accent,TextAlign=ContentAlignment.MiddleLeft};clean.LinkClicked+=async(_,_)=>await CleanMissing();side.Controls.Add(clean,0,9);work.Panel1.Controls.Add(side);

        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(20,18,20,10)};
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,54));body.RowStyles.Add(new RowStyle(SizeType.Absolute,43));body.RowStyles.Add(new RowStyle(SizeType.Absolute,34));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var searchLine=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,GrowStyle=TableLayoutPanelGrowStyle.FixedSize,Padding=Padding.Empty,Margin=Padding.Empty};searchLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));searchLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));searchLine.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        query.Font=new Font(Font.FontFamily,13);query.Margin=new Padding(0,3,12,6);var searchButton=new SearchActionButton{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,6)};searchButton.Click+=async(_,_)=>await Search();
        searchLine.Controls.Add(query,0,0);searchLine.Controls.Add(searchButton,1,0);body.Controls.Add(searchLine,0,0);
        kind.Items.AddRange(["전체","엑셀","PDF","한글","Word","PowerPoint","텍스트"]);kind.SelectedIndex=0;
        order.Items.AddRange(["관련도순","최근 수정순"]);order.SelectedIndex=0;
        var filters=Flow();filters.Padding=new Padding(0,5,0,0);kind.Margin=new Padding(0,0,8,0);order.Margin=new Padding(0,0,12,0);namesOnly.Margin=new Padding(0,4,8,0);bookmarks.Margin=new Padding(0,4,8,0);watchChanges.Margin=new Padding(0,4,0,0);filters.Controls.AddRange([kind,order,namesOnly,bookmarks,watchChanges]);body.Controls.Add(filters,0,1);
        var summary=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));summary.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));
        count.ForeColor=muted;summary.Controls.Add(count,0,0);var export=new LinkLabel{Text="결과 목록 저장",Dock=DockStyle.Fill,LinkColor=accent,TextAlign=ContentAlignment.MiddleRight};export.LinkClicked+=(_,_)=>ExportResults();summary.Controls.Add(export,1,0);body.Controls.Add(summary,0,2);
        resultSplit.Panel1MinSize=250;resultSplit.Panel2MinSize=250;
        ConfigureGrid();resultSplit.Panel1.Controls.Add(grid);BuildDetails();body.Controls.Add(resultSplit,0,3);work.Panel2.Controls.Add(body);

        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty,Padding=new Padding(20,10,20,14),BackColor=Color.FromArgb(246,248,251)};
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute,64));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,28));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,10));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        progressText.Font=new Font(Font.FontFamily,10);current.Font=new Font(Font.FontFamily,9);current.ForeColor=muted;current.AutoEllipsis=true;
        statistics.Font=new Font(Font.FontFamily,9.5F);statistics.ForeColor=ink;statistics.TextAlign=ContentAlignment.TopLeft;statistics.AccessibleName="색인 통계와 마지막 색인 시간";
        foreach(var control in new Control[]{statistics,progressText,bar,current})control.Margin=Padding.Empty;
        footer.Controls.Add(statistics,0,0);footer.Controls.Add(progressText,0,1);footer.Controls.Add(bar,0,2);footer.Controls.Add(current,0,3);shell.Controls.Add(footer,0,2);
        tips.SetToolTip(query,"띄어쓰기로 여러 단어를 입력하면 모두 포함된 문서를 찾습니다. 따옴표로 묶으면 문구 그대로 찾습니다. Ctrl+F로 검색창 이동.");
        tips.SetToolTip(online,"OneDrive·Google Drive 등의 온라인 전용 파일도 내용 색인합니다. 인터넷 상태에 따라 오래 걸릴 수 있습니다.");
    }
    private void ConfigureGrid()
    {
        grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(239,243,248);grid.ColumnHeadersDefaultCellStyle.ForeColor=ink;
        grid.ColumnHeadersDefaultCellStyle.Font=new Font(Font.FontFamily,9,FontStyle.Bold);grid.ColumnHeadersHeight=38;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.DefaultCellStyle.Font=new Font(Font.FontFamily,9.5F);grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(224,234,255);grid.DefaultCellStyle.SelectionForeColor=ink;
        grid.DefaultCellStyle.Padding=new Padding(6,3,6,3);grid.GridColor=Color.FromArgb(227,232,239);grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;
        grid.RowTemplate.Height=58;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.ShowCellToolTips=true;
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Name",HeaderText="파일명",FillWeight=62,MinimumWidth=180,DefaultCellStyle=new DataGridViewCellStyle{WrapMode=DataGridViewTriState.True}});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Kind",HeaderText="유형",FillWeight=12,MinimumWidth=65});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Modified",HeaderText="수정일",FillWeight=23,MinimumWidth=90});
        foreach(DataGridViewColumn col in grid.Columns)col.SortMode=DataGridViewColumnSortMode.NotSortable;
        grid.SelectionChanged+=(_,_)=>SelectCurrent();grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0)OpenSelected(false);};
        grid.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;OpenSelected(false);}};
        var context=new ContextMenuStrip();context.Items.Add("파일 열기",null,(_,_)=>OpenSelected(false));context.Items.Add("폴더 열기",null,(_,_)=>OpenSelected(true));context.Items.Add("문서 변환",null,(_,_)=>OpenConversion());context.Items.Add("경로 복사",null,(_,_)=>CopyPath());grid.ContextMenuStrip=context;
        context.Items.Add(new ToolStripSeparator());
        context.Items.Add("본문 Markdown 저장",null,(_,_)=>{if(selected is not null)try{DocumentTools.Export(this,selected);}catch(Exception ex){MessageBox.Show(this,ex.Message);}});
        context.Items.Add("북마크 설정·해제",null,async(_,_)=>{if(selected is null)return;var note=store.GetNote(selected.Path);store.SetNote(selected.Path,!note.Bookmark,note.Tags);await Search();});
        context.Items.Add("태그 편집",null,(_,_)=>{if(selected is null)return;var note=store.GetNote(selected.Path);var tags=DocumentTools.EditTags(this,note.Tags);if(tags is not null)store.SetNote(selected.Path,note.Bookmark,tags);});
        context.Items.Add("비교 대상으로 선택",null,(_,_)=>{comparison=selected;progressText.Text=selected is null?"문서를 먼저 선택하세요.":"비교 대상: "+selected.Name;});
        context.Items.Add("선택 문서와 비교",null,(_,_)=>{if(selected is null||comparison is null){MessageBox.Show(this,"먼저 ‘비교 대상으로 선택’으로 이전 문서를 지정하세요.");return;}try{DocumentTools.ShowText(this,"문서 비교",DocumentComparison.Compare(comparison.Content,selected.Content,comparison.Name,selected.Name));}catch(Exception ex){MessageBox.Show(this,ex.Message);}});
    }
    private void BuildDetails()
    {
        var detailLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(14,0,0,0),Margin=Padding.Empty,BackColor=Color.White,AutoScroll=true,AutoScrollMinSize=new Size(0,365)};
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(var h in new float[]{62,65,26,50,40,32})p.RowStyles.Add(new RowStyle(SizeType.Absolute,h));p.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        wholeText.Dock=DockStyle.Fill;wholeText.MinimumSize=new Size(0,32);wholeText.Margin=new Padding(14,6,0,6);
        detailTitle.Font=new Font(Font.FontFamily,11,FontStyle.Bold);detailTitle.AutoEllipsis=true;detailTitle.TextAlign=ContentAlignment.TopLeft;
        detailPath.Font=new Font(Font.FontFamily,9);detailInfo.Font=new Font(Font.FontFamily,9);detailInfo.ForeColor=muted;detailStatus.Font=new Font(Font.FontFamily,9);detailStatus.ForeColor=muted;
        p.Controls.Add(detailTitle,0,0);p.Controls.Add(detailPath,0,1);p.Controls.Add(detailInfo,0,2);p.Controls.Add(detailStatus,0,3);
        var actions=Flow();open.MinimumSize=new Size(78,34);folder.MinimumSize=new Size(78,34);copy.MinimumSize=new Size(78,34);actions.Controls.AddRange([open,folder,copy]);p.Controls.Add(actions,0,4);
        var hint=Label("선택한 문서의 내용과 시트");hint.Font=new Font(Font.FontFamily,9,FontStyle.Bold);p.Controls.Add(hint,0,5);
        var tabs=new TabControl{Dock=DockStyle.Fill,AccessibleName="문서 미리보기 종류"};var textTab=new TabPage("일치 내용"){Padding=new Padding(8)};textTab.Controls.Add(preview);var sheetTab=new TabPage("엑셀 시트"){Padding=new Padding(8)};sheetTab.Controls.Add(sheets);tabs.TabPages.AddRange([textTab,sheetTab]);p.Controls.Add(tabs,0,6);
        detailLayout.Controls.Add(p,0,0);detailLayout.Controls.Add(wholeText,0,1);resultSplit.Panel2.Controls.Add(detailLayout);
        wholeText.CheckedChanged+=(_,_)=>ShowPreview();open.Enabled=folder.Enabled=copy.Enabled=false;
    }
    private void WireEvents()
    {
        debounce.Tick+=async(_,_)=>{debounce.Stop();await Search();};query.TextChanged+=(_,_)=>ScheduleSearch();
        query.KeyDown+=async(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;debounce.Stop();await Search();}};
        kind.SelectedIndexChanged+=(_,_)=>ScheduleSearch();order.SelectedIndexChanged+=(_,_)=>ScheduleSearch();namesOnly.CheckedChanged+=(_,_)=>ScheduleSearch();
        bookmarks.CheckedChanged+=(_,_)=>ScheduleSearch();
        start.Click+=async(_,_)=>await StartScan();stop.Click+=(_,_)=>{scanCts?.Cancel();stop.Enabled=false;progressText.Text="현재 작업을 중지하고 있습니다…";};
        open.Click+=(_,_)=>OpenSelected(false);folder.Click+=(_,_)=>OpenSelected(true);copy.Click+=(_,_)=>CopyPath();
        duringScan.Tick+=async(_,_)=>await Search();
        FormClosing+=async(_,e)=>
        {
            if(scanTask is {IsCompleted:false}&&!closing)
            {
                e.Cancel=true;closing=true;scanCts?.Cancel();try{await scanTask;}catch{};Close();
            }
        };
        FormClosed+=(_,_)=>{watcher?.Dispose();debounce.Dispose();duringScan.Dispose();tips.Dispose();scanCts?.Dispose();};
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(keyData==(Keys.Control|Keys.F)){query.Focus();query.SelectAll();return true;}
        if(keyData==Keys.F5){if(start.Enabled)_=StartScan();return true;}
        return base.ProcessCmdKey(ref msg,keyData);
    }
    private void LoadSettings()
    {
        var saved=store.GetSetting("roots");
        if(saved is not null){try{SetRoots(JsonSerializer.Deserialize<string[]>(saved)??[]);}catch(JsonException){}}
        if(roots.Items.Count==0)
        {
            var defaults=new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")};
            SetRoots(defaults.Where(Directory.Exists));
        }
        online.Checked=store.GetSetting("online")!="false";UpdateStats();
    }
    private void SetRoots(IEnumerable<string> paths){roots.Items.Clear();foreach(var p in TextRules.ReduceRoots(paths))roots.Items.Add(p);}
    private void SaveRoots(){store.SetSetting("roots",JsonSerializer.Serialize(roots.Items.Cast<string>().ToArray()));watcher?.Configure(roots.Items.Cast<string>(),watchChanges.Checked);}
    private void AddFolder()
    {
        using var dialog=new FolderBrowserDialog{Description="검색할 문서가 있는 폴더를 선택하세요.",UseDescriptionForTitle=true,ShowNewFolderButton=false};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;SetRoots(roots.Items.Cast<string>().Append(dialog.SelectedPath));SaveRoots();
    }
    private void ScheduleSearch(){debounce.Stop();debounce.Start();}
    private async Task Search()
    {
        if(IsDisposed)return;var version=++searchVersion;var onlyBookmarks=bookmarks.Checked;var tag=query.Text.StartsWith('#')?query.Text[1..].Trim():null;var options=new SearchOptions(tag is null?query.Text:"",kind.SelectedItem?.ToString()??"전체",namesOnly.Checked,order.SelectedIndex==1,OnlyBookmarked:onlyBookmarks,Tag:tag);
        try
        {
            var result=await Task.Run(()=>{var list=store.Search(options,out var total);return(list,total);});
            if(IsDisposed||version!=searchVersion)return;
            var preserve=selected?.Path;hits=result.list;grid.Rows.Clear();
            foreach(var h in hits)
            {
                var i=grid.Rows.Add(h.Name,TextRules.Kind(h.Extension),new DateTime(h.ModifiedTicks,DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd"));
                grid.Rows[i].Tag=h;grid.Rows[i].Cells[0].ToolTipText=h.Path+"\n색인 상태: "+h.Status;
            }
            if(result.total==0)count.Text=query.Text.Length==0?"색인을 시작하면 이곳에 문서가 표시됩니다.":"일치하는 문서가 없습니다. 단어를 줄이거나 색인을 갱신해 보세요.";
            else count.Text=$"{result.total:N0}개 일치"+(result.total>hits.Count?$" · 상위 {hits.Count:N0}개 표시":"")+" · 단어를 띄우면 모두 포함하여 검색";
            var row=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>((SearchHit?)r.Tag)?.Path==preserve);
            if(row is not null)grid.CurrentCell=row.Cells[0];SelectCurrent();UpdateStats();
        }
        catch(Exception ex){if(!IsDisposed)count.Text="검색하지 못했습니다. 다시 시도해 주세요. "+ex.Message;}
    }
    private void SelectCurrent()
    {
        selected=grid.CurrentRow?.Tag as SearchHit;open.Enabled=folder.Enabled=copy.Enabled=selected is not null;
        if(selected is null){detailTitle.Text="문서를 선택하세요.";detailPath.Text="";detailInfo.Text="";detailStatus.Text="";preview.Text="파일을 선택하면 일치 내용과 저장 위치를 볼 수 있습니다.";sheets.Items.Clear();return;}
        detailTitle.Text=selected.Name;detailPath.Text=selected.Path;
        detailInfo.Text=$"{TextRules.Kind(selected.Extension)} · {selected.Size/1024d:N0} KB · "+new DateTime(selected.ModifiedTicks,DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd");
        detailStatus.Text="내용 색인: "+selected.Status+(selected.Note.Length>0?"\n"+selected.Note:"");
        tips.SetToolTip(detailStatus,detailStatus.Text);sheets.Items.Clear();if(selected.Sheets.Length>0)sheets.Items.AddRange(selected.Sheets.Split('\n'));else sheets.Items.Add("시트 정보가 없습니다.");ShowPreview();
    }
    private void ShowPreview()
    {
        if(selected is null)return;var terms=TextRules.Terms(query.Text);
        preview.Text=wholeText.Checked?selected.Content:TextRules.Snippet(selected.Content,terms);
        if(preview.Text.Length==0)preview.Text="본문 색인이 없습니다. 색인 상태와 안내를 확인하세요.";
        foreach(var term in terms)
        {
            var at=0;var marked=0;
            while(at<preview.TextLength && marked++<150)
            {
                var index=preview.Find(term,at,RichTextBoxFinds.None);if(index<0)break;
                preview.Select(index,term.Length);preview.SelectionBackColor=Color.FromArgb(255,239,165);preview.SelectionColor=Color.FromArgb(35,40,48);at=index+term.Length;
            }
        }
        preview.Select(0,0);preview.ScrollToCaret();
    }
    private async Task StartScan()
    {
        if(scanTask is {IsCompleted:false})return;
        if(roots.Items.Count==0){MessageBox.Show(this,"폴더 추가 또는 PC 전체 선택으로 색인할 위치를 지정하세요.","색인할 위치가 없습니다.");return;}
        SaveRoots();store.SetSetting("online",online.Checked?"true":"false");scanCts=new CancellationTokenSource();start.Enabled=false;stop.Enabled=true;online.Enabled=false;
        var paths=roots.Items.Cast<string>().ToArray();var options=new ScanOptions(online.Checked);duringScan.Start();bar.Style=ProgressBarStyle.Marquee;
        var progress=new Progress<ScanProgress>(p=>
        {
            if(IsDisposed)return;if((DateTime.UtcNow-lastProgress).TotalMilliseconds<160&&!p.Finished)return;lastProgress=DateTime.UtcNow;
            progressText.Text=p.Phase=="파일 목록 확인"?$"파일 목록 확인 중 · {p.Found:N0}개 발견":$"{p.Phase} · {p.Processed:N0}/{p.Found:N0}개 · 갱신 {p.Updated:N0} · 변경 없음 {p.Skipped:N0} · 확인 필요 {p.Issues:N0}";
            current.Text=p.Current;
            if(p.Phase!="파일 목록 확인"){bar.Style=ProgressBarStyle.Continuous;bar.Maximum=Math.Max(1,p.Found);bar.Value=Math.Min(p.Processed,bar.Maximum);}
        });
        scanTask=Task.Run(()=>Indexer.Run(store,paths,options,progress,scanCts.Token));
        try{await scanTask;}
        catch(OperationCanceledException){progressText.Text="색인을 중지했습니다. 저장된 내용은 계속 검색할 수 있습니다.";}
        catch(Exception ex){progressText.Text="색인을 완료하지 못했습니다. "+ex.Message;}
        finally
        {
            duringScan.Stop();if(!IsDisposed){start.Enabled=true;stop.Enabled=false;online.Enabled=true;bar.Style=ProgressBarStyle.Continuous;UpdateStats();if(!closing)await Search();}
            scanCts.Dispose();scanCts=null;
        }
    }
    private void UpdateStats()
    {
        var s=store.Stats();statistics.Text=$"내 PC 색인 {s.Total:N0}개 · 내용 읽음 {s.Complete:N0}개 · 파일명만/대기 {s.Pending:N0}개 · 확인 필요 {s.Issues:N0}개"
            +(store.GetSetting("lastScan") is { } date?"\n마지막 색인: "+date:"\n아직 내용 색인을 실행하지 않았습니다.");
    }
    private async Task CleanMissing()
    {
        if(scanTask is {IsCompleted:false}){progressText.Text="색인이 끝난 뒤 정리할 수 있습니다.";return;}
        try{var paths=roots.Items.Cast<string>().ToArray();var n=await Task.Run(()=>store.RemoveMissing(paths,CancellationToken.None));progressText.Text=$"실제로 없어진 파일 {n:N0}개의 색인을 정리했습니다. 원본 문서는 변경하지 않습니다.";await Search();}
        catch(Exception ex){progressText.Text="색인을 정리하지 못했습니다. "+ex.Message;}
    }
    private async Task CheckUpdatesInBackground()
    {
        // At most once a day. A failed check never blocks local search or opens a dialog.
        if(DateTimeOffset.TryParse(store.GetSetting("lastUpdateCheck"),out var previous)&&DateTimeOffset.UtcNow-previous<TimeSpan.FromDays(1))return;
        try
        {
            using var client=new GitHubReleaseClient();var release=await client.CheckAsync(AppBrand.Version);
            if(IsDisposed)return;store.SetSetting("lastUpdateCheck",DateTimeOffset.UtcNow.ToString("O"));
            availableRelease=release;if(release is not null){update.Text="새 버전 "+release.Version;tips.SetToolTip(update,"새 버전을 다운로드하고 설치할 수 있습니다.");}
        }
        catch{/* Offline use remains available; manual checks show actionable errors. */}
    }
    private void OpenConversion()
    {
        using var dialog=new ConversionForm(selected is not null&&File.Exists(selected.Path)?[selected.Path]:[]);
        dialog.ShowDialog(this);
    }
    private async Task OpenUpdates()
    {
        using var dialog=new UpdateForm(availableRelease);
        if(dialog.ShowDialog(this)!=DialogResult.OK||dialog.InstallerPath is null)return;
        update.Enabled=false;start.Enabled=false;
        try
        {
            if(scanTask is {IsCompleted:false})
            {
                progressText.Text="설치 전에 색인을 중지하고 저장하고 있습니다…";scanCts?.Cancel();
                try{await scanTask;}catch(OperationCanceledException){}
            }
            var info=new ProcessStartInfo(dialog.InstallerPath){UseShellExecute=true};
            info.ArgumentList.Add("/UPDATE");info.ArgumentList.Add("/SILENT");info.ArgumentList.Add("/SUPPRESSMSGBOXES");info.ArgumentList.Add("/NORESTART");info.ArgumentList.Add("/WAITPID="+Environment.ProcessId);
            _=Process.Start(info)??throw new IOException("설치 프로그램을 시작하지 못했습니다.");
            Close();
        }
        catch(Exception ex){update.Enabled=true;start.Enabled=true;MessageBox.Show(this,"설치를 시작하지 못했습니다. 현재 버전을 계속 사용할 수 있습니다.\n\n"+ex.Message,"모두찾아줘 업데이트");}
    }
    private void OpenSelected(bool reveal)
    {
        if(selected is null)return;
        try
        {
            var path=reveal?Path.GetDirectoryName(selected.Path)!:selected.Path;
            if(!File.Exists(path)&&!Directory.Exists(path)){MessageBox.Show(this,"파일이 이동되었거나 드라이브 연결이 끊겼습니다. 저장 위치를 확인하거나 다시 색인해 주세요.","위치를 열 수 없습니다.");return;}
            Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
        }
        catch(Exception ex){MessageBox.Show(this,"열지 못했습니다.\n"+ex.Message,"모두찾아줘");}
    }
    private void CopyPath()
    {
        if(selected is null)return;
        try{Clipboard.SetText(selected.Path);progressText.Text="선택한 문서의 전체 경로를 복사했습니다.";}catch(System.Runtime.InteropServices.ExternalException){progressText.Text="다른 프로그램이 클립보드를 사용 중입니다. 다시 눌러 주세요.";}
    }
    private void ExportResults()
    {
        if(hits.Count==0){count.Text="저장할 검색 결과가 없습니다.";return;}
        using var dialog=new SaveFileDialog{Title="현재 표시된 검색 결과 저장",FileName="모두찾아줘_검색결과_"+DateTime.Now.ToString("yyyyMMdd")+".csv",Filter="CSV 파일|*.csv"};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        static string Cell(string s)=>"\""+((s.Length>0&&"=+-@".Contains(s[0]))?"'"+s:s).Replace("\"","\"\"")+"\"";
        try
        {
            using var writer=new StreamWriter(dialog.FileName,false,new UTF8Encoding(true));writer.WriteLine("파일명,유형,경로,시트,내용색인,안내");
            foreach(var h in hits)writer.WriteLine(string.Join(",",new[]{h.Name,TextRules.Kind(h.Extension),h.Path,h.Sheets.Replace('\n','|'),h.Status,h.Note}.Select(Cell)));
            count.Text=$"현재 표시된 {hits.Count:N0}개 결과를 저장했습니다.";
        }
        catch(Exception ex){MessageBox.Show(this,"결과를 저장하지 못했습니다.\n"+ex.Message,"모두찾아줘");}
    }
}
