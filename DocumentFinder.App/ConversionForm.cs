using System.Diagnostics;
using DocumentFinder.Core;

namespace DocumentFinder.App;

internal sealed class ConversionForm:Form
{
    private readonly FileeConversionClient client=CreateClient();
    private readonly DataGridView files=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,MultiSelect=true,
        SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,AccessibleName="변환할 파일"};
    private readonly ComboBox target=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,AccessibleName="변환할 형식"};
    private readonly TextBox output=new(){Dock=DockStyle.Fill,ReadOnly=true,AccessibleName="변환 결과 저장 위치"};
    private readonly TextBox pages=new(){Width=170,PlaceholderText="전체 쪽 (예: 1-3,5)",AccessibleName="PDF 쪽수"};
    private readonly TextBox details=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,AccessibleName="선택한 변환 결과 안내"};
    private readonly Label status=new(){Text="변환 엔진을 확인하고 있습니다…",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
    private readonly ProgressBar progress=new(){Dock=DockStyle.Fill};
    private readonly Button add=ActionButton("파일 추가"),remove=ActionButton("선택 제거"),clear=ActionButton("목록 비우기"),engines=ActionButton("추가 엔진"),
        browse=ActionButton("폴더 선택"),convert=ActionButton("변환 시작"),cancel=ActionButton("중지"),open=ActionButton("결과 열기"),folder=ActionButton("결과 폴더"),close=ActionButton("닫기");
    private IReadOnlyList<ConversionFormat> formats=[];
    private readonly Dictionary<string,ConversionFileResult> results=new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? job,route;
    private Task? running;
    private string? resultFolder;
    private int routeVersion;
    private bool closing;
    private readonly string[] initial;
    internal static FileeConversionClient CreateClient()=>new(Environment.GetEnvironmentVariable("MODUFIND_FILEE_HOME")??Path.Combine(AppContext.BaseDirectory,"conversion","filee-"+FileeConversionClient.EngineVersion),
        Path.Combine(Program.DataDirectory,"conversion-filee"));
    private static Button ActionButton(string text)=>new(){Text=text,AutoSize=true,MinimumSize=new(85,34),Margin=new Padding(0,0,8,0)};
    public ConversionForm(IEnumerable<string>? initialFiles=null)
    {
        initial=initialFiles?.ToArray()??[];AutoScaleDimensions=new(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        Text="모두찾아줘 · 문서 변환";Icon=AppBrand.LoadIcon();Font=new("맑은 고딕",10);Size=new(1100,830);MinimumSize=new(960,720);StartPosition=FormStartPosition.CenterParent;AllowDrop=true;
        Build();cancel.Enabled=convert.Enabled=open.Enabled=folder.Enabled=false;
        add.Click+=async(_,_)=>{using var dialog=new OpenFileDialog{Title="변환할 문서 선택",Multiselect=true,Filter="문서 및 이미지|"+string.Join(";",formats.SelectMany(f=>f.Extensions).Distinct().Select(e=>"*."+e))+"|모든 파일|*.*"};if(dialog.ShowDialog(this)==DialogResult.OK)await AddFiles(dialog.FileNames);};
        remove.Click+=async(_,_)=>{foreach(var row in files.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(r=>r.Index).ToList())files.Rows.Remove(row);await UpdateTargets();};
        clear.Click+=async(_,_)=>{files.Rows.Clear();results.Clear();details.Clear();await UpdateTargets();};
        browse.Click+=(_,_)=>{using var dialog=new FolderBrowserDialog{Description="변환 결과를 저장할 상위 폴더",UseDescriptionForTitle=true,ShowNewFolderButton=true};if(dialog.ShowDialog(this)==DialogResult.OK)output.Text=dialog.SelectedPath;};
        engines.Click+=async(_,_)=>{using var dialog=new ConversionEnginesForm(client);dialog.ShowDialog(this);await UpdateTargets();};
        convert.Click+=async(_,_)=>await StartConversion();cancel.Click+=(_,_)=>{job?.Cancel();cancel.Enabled=false;status.Text="변환을 중지하고 있습니다. 원본과 완료된 결과는 유지됩니다.";};
        files.SelectionChanged+=(_,_)=>ShowSelection();open.Click+=(_,_)=>OpenResult();folder.Click+=(_,_)=>{if(resultFolder is not null&&Directory.Exists(resultFolder))Launch(resultFolder);};close.Click+=(_,_)=>Close();
        DragEnter+=(_,e)=>{e.Effect=job is null&&e.Data?.GetDataPresent(DataFormats.FileDrop)==true?DragDropEffects.Copy:DragDropEffects.None;};
        DragDrop+=async(_,e)=>{if(job is null&&e.Data?.GetData(DataFormats.FileDrop) is string[] paths)await AddFiles(paths);};
        Shown+=async(_,_)=>
        {
            try{formats=await client.FormatsAsync();await AddFiles(initial);status.Text=files.Rows.Count==0?"파일을 추가하거나 이 창에 끌어놓으세요.":$"{files.Rows.Count}개 파일을 추가했습니다.";}
            catch(Exception ex){status.Text="변환 엔진을 열지 못했습니다.";details.Text=ex.Message;add.Enabled=engines.Enabled=false;}
        };
        FormClosing+=async(_,e)=>
        {
            if(running is {IsCompleted:false}&&!closing){e.Cancel=true;closing=true;job?.Cancel();try{await running;}catch{};Close();}
            route?.Cancel();
        };
        FormClosed+=(_,_)=>{route?.Cancel();route?.Dispose();job?.Dispose();};
    }
    private void Build()
    {
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=11};
        foreach(var height in new[]{38,46,42})layout.RowStyles.Add(new(SizeType.Absolute,height));
        layout.RowStyles.Add(new(SizeType.Percent,100));foreach(var height in new[]{42,44,38,70,30})layout.RowStyles.Add(new(SizeType.Absolute,height));
        layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(new Label{Text="문서 변환",Font=new Font(Font,FontStyle.Bold),Dock=DockStyle.Fill},0,0);
        layout.Controls.Add(new Label{Text="Filee 2.4.1 엔진으로 이 PC에서 변환합니다. 원본은 유지하고 결과는 새 폴더에 저장합니다.",Dock=DockStyle.Fill},0,1);
        var tools=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};tools.Controls.AddRange([add,remove,clear,engines]);layout.Controls.Add(tools,0,2);
        files.Columns.Add(new DataGridViewTextBoxColumn{Name="Name",HeaderText="파일명",FillWeight=32,MinimumWidth=200});
        files.Columns.Add(new DataGridViewTextBoxColumn{Name="Type",HeaderText="입력 형식",FillWeight=12,MinimumWidth=80});
        files.Columns.Add(new DataGridViewTextBoxColumn{Name="State",HeaderText="상태",FillWeight=15,MinimumWidth=100});
        files.Columns.Add(new DataGridViewTextBoxColumn{Name="Result",HeaderText="결과 / 안내",FillWeight=41,MinimumWidth=180});
        files.RowTemplate.Height=40;foreach(DataGridViewColumn column in files.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;layout.Controls.Add(files,0,3);
        var formatLine=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};formatLine.RowStyles.Add(new(SizeType.Percent,100));
        formatLine.ColumnStyles.Add(new(SizeType.Absolute,102));formatLine.ColumnStyles.Add(new(SizeType.Percent,100));formatLine.ColumnStyles.Add(new(SizeType.Absolute,92));formatLine.ColumnStyles.Add(new(SizeType.Absolute,190));
        formatLine.Controls.Add(new Label{Text="출력 형식",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);formatLine.Controls.Add(target,1,0);formatLine.Controls.Add(new Label{Text="PDF 쪽수",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},2,0);formatLine.Controls.Add(pages,3,0);layout.Controls.Add(formatLine,0,4);
        var destination=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};destination.RowStyles.Add(new(SizeType.Percent,100));destination.ColumnStyles.Add(new(SizeType.Absolute,102));destination.ColumnStyles.Add(new(SizeType.Percent,100));destination.ColumnStyles.Add(new(SizeType.Absolute,145));
        destination.Controls.Add(new Label{Text="저장 위치",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);destination.Controls.Add(output,1,0);browse.Dock=DockStyle.Fill;destination.Controls.Add(browse,2,0);layout.Controls.Add(destination,0,5);
        layout.Controls.Add(new Label{Text="복잡한 서식·차트는 달라질 수 있으므로 결과를 확인하세요. ‘추가 엔진 필요’ 형식은 추가 엔진에서 설치할 수 있습니다.",Dock=DockStyle.Fill},0,6);
        layout.Controls.Add(details,0,7);layout.Controls.Add(progress,0,8);
        status.AutoSize=true;status.MinimumSize=new Size(0,32);status.Margin=Padding.Empty;layout.Controls.Add(status,0,9);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty,Padding=new Padding(0,4,0,4)};
        actions.Controls.AddRange([close,folder,open,cancel,convert]);layout.Controls.Add(actions,0,10);Controls.Add(layout);
        output.Text=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
    private async Task AddFiles(IEnumerable<string> paths)
    {
        if(formats.Count==0||job is not null)return;var known=files.Rows.Cast<DataGridViewRow>().Select(r=>(string)r.Tag!).ToHashSet(StringComparer.OrdinalIgnoreCase);var ignored=new List<string>();
        foreach(var path in paths)
        {
            if(!File.Exists(path)||FileeConversionClient.FindFormat(formats,path) is not { } format){ignored.Add(Path.GetFileName(path));continue;}
            var full=Path.GetFullPath(path);if(!known.Add(full))continue;
            var row=files.Rows.Add(Path.GetFileName(full),format.Name,"대기","");files.Rows[row].Tag=full;files.Rows[row].Cells[0].ToolTipText=full;
        }
        if(ignored.Count>0)details.Text="지원하지 않거나 찾을 수 없는 파일: "+string.Join(", ",ignored);
        await UpdateTargets();
        if(ignored.Count==0)ShowSelection();
    }
    private async Task UpdateTargets()
    {
        var version=++routeVersion;route?.Cancel();route?.Dispose();route=new();var token=route.Token;convert.Enabled=false;
        var chosen=(target.SelectedItem as ConversionTarget)?.Id;var inputs=files.Rows.Cast<DataGridViewRow>().Select(r=>(string)r.Tag!).ToArray();
        try
        {
            status.Text="변환 가능한 형식을 확인하고 있습니다…";var available=await client.TargetsAsync(inputs,token);
            if(IsDisposed||version!=routeVersion)return;target.Items.Clear();target.Items.AddRange(available.Cast<object>().ToArray());
            var selection=available.FirstOrDefault(t=>t.Id==chosen)??available.FirstOrDefault(t=>t.Id=="pdf")??available.FirstOrDefault();if(selection is not null)target.SelectedItem=selection;
            convert.Enabled=selection is not null&&inputs.Length>0;status.Text=inputs.Length==0?"파일을 추가하거나 끌어놓으세요.":selection is null?"모든 입력 파일에 공통인 출력 형식이 없습니다.":$"{inputs.Length}개 파일 · 출력 형식을 선택한 뒤 변환하세요.";
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!IsDisposed&&version==routeVersion){status.Text="출력 형식을 확인하지 못했습니다.";details.Text=ex.Message;}}
    }
    private async Task StartConversion()
    {
        if(job is not null||target.SelectedItem is not ConversionTarget selected)return;
        if(string.IsNullOrWhiteSpace(output.Text)){MessageBox.Show(this,"저장 위치를 선택하세요.","문서 변환");return;}
        job=new();SetBusy(true);results.Clear();progress.Value=0;var inputs=files.Rows.Cast<DataGridViewRow>().Select(r=>(string)r.Tag!).ToArray();
        foreach(DataGridViewRow row in files.Rows){row.Cells[2].Value="대기";row.Cells[3].Value="";}
        var completed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var updates=new Progress<ConversionProgress>(p=>
        {
            if(IsDisposed||job is null)return;var row=files.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>(string)r.Tag! == p.Source);if(row is null)return;
            if(p.State is "done" or "failed" or "cancelled")completed.Add(p.Source);
            row.Cells[2].Value=p.State=="done"?"완료":p.State=="failed"?"실패":p.State=="cancelled"?"중지":$"변환 중 {p.Percent}%";
            progress.Value=Math.Clamp((int)((completed.Count+(p.State=="progress"?p.Percent/100d:0))*100/inputs.Length),0,100);
            status.Text=$"{completed.Count}/{inputs.Length}개 처리 · "+Path.GetFileName(p.Source);
        });
        running=Run();
        async Task Run()
        {
            try
            {
                var result=await client.ConvertAsync(inputs,selected.Id,output.Text,pages.Text,updates,job.Token);resultFolder=result.Directory;
                foreach(var item in result.Files)
                {
                    results[item.Source]=item;var row=files.Rows.Cast<DataGridViewRow>().First(r=>(string)r.Tag! == item.Source);
                    row.Cells[2].Value=item.State=="done"?"완료":item.State=="cancelled"?"중지":"실패";row.Cells[3].Value=item.Outputs.Length>0?string.Join(", ",item.Outputs.Select(Path.GetFileName)):item.Message;row.Cells[3].ToolTipText=item.Message;
                }
                status.Text=$"완료 {result.Files.Count(f=>f.State=="done")}개 · 실패 {result.Files.Count(f=>f.State=="failed")}개 · 중지 {result.Files.Count(f=>f.State=="cancelled")}개";progress.Value=result.Cancelled?progress.Value:100;
                folder.Enabled=Directory.Exists(resultFolder);ShowSelection();
            }
            catch(OperationCanceledException){status.Text="변환을 중지했습니다.";}
            catch(Exception ex){status.Text="변환을 완료하지 못했습니다.";details.Text=ex.Message;}
            finally{job?.Dispose();job=null;if(!IsDisposed)SetBusy(false);}
        }
        await running;
    }
    private void SetBusy(bool busy)
    {
        foreach(var control in new Control[]{add,remove,clear,engines,browse,target,pages,convert})control.Enabled=!busy;
        convert.Enabled=!busy&&target.SelectedItem is not null&&files.Rows.Count>0;cancel.Enabled=busy;close.Text=busy?"중지 후 닫기":"닫기";AllowDrop=!busy;
    }
    private void ShowSelection()
    {
        var source=files.CurrentRow?.Tag as string;open.Enabled=source is not null&&results.TryGetValue(source,out var r)&&r.Outputs.Length>0;
        if(source is null)return;
        details.Text=results.TryGetValue(source,out var item)?source+"\r\n"+string.Join("\r\n",item.Outputs)+(item.Message.Length>0?"\r\n"+item.Message:""):source;
    }
    private void OpenResult(){if(files.CurrentRow?.Tag is string source&&results.TryGetValue(source,out var item)&&item.Outputs.FirstOrDefault() is { } path)Launch(path);}
    private void Launch(string path){try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,ex.Message,"열지 못했습니다.");}}
}
