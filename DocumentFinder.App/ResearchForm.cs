using System.Diagnostics;
using DocumentFinder.Core;

namespace DocumentFinder.App;

internal sealed class ResearchForm:Form
{
    private readonly SearchStore store;
    private readonly TextBox question=new(){Dock=DockStyle.Fill,Multiline=true,AccessibleName="자연어 질문",PlaceholderText="예: 내 문서에서 설치 일정 찾아줘 / 개인정보보호법 제15조 / 대전 한밭초등학교 학생수 / 대전 창업 지원사업"};
    private readonly ComboBox scope=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=190,AccessibleName="검색할 자료"};
    private readonly Button ask=Action("근거 검색"),cancel=Action("중지"),settings=Action("연결 설정"),ai=Action("로컬 AI 요약"),export=Action("MD 저장"),open=Action("원문 열기");
    private readonly Label status=new(){Text="검색할 자료와 질문을 입력하세요.",AutoSize=true,Dock=DockStyle.Fill};
    private readonly DataGridView sources=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,AccessibleName="답변의 원문 근거"};
    private readonly RichTextBox answer=new(){Dock=DockStyle.Fill,ReadOnly=true,DetectUrls=false,BackColor=Color.White,AccessibleName="원문 근거 답변"};
    private readonly RichTextBox original=new(){Dock=DockStyle.Fill,ReadOnly=true,DetectUrls=false,BackColor=Color.White,AccessibleName="선택한 원문 발췌"};
    private CancellationTokenSource? operation;
    private ResearchResult? result;
    private Task? running;
    private bool closing;
    private static Button Action(string text)=>new(){Text=text,AutoSize=true,MinimumSize=new(100,36),Margin=new Padding(0,0,8,4)};
    private static readonly (string Name,ResearchDomain Domain)[] Domains=[("내 PC 문서",ResearchDomain.Local),("법령·판례",ResearchDomain.Law),("학교알리미·NEIS",ResearchDomain.School),("국가통계 KOSIS",ResearchDomain.Statistics),("기업·창업 지원사업",ResearchDomain.Support),("기업 공시·재무 DART",ResearchDomain.Dart),("자동 분류 (공공조회 포함)",ResearchDomain.Auto)];
    public ResearchForm(SearchStore store,string initialQuestion="")
    {
        this.store=store;AutoScaleDimensions=new(96,96);AutoScaleMode=AutoScaleMode.Dpi;Font=new("맑은 고딕",10);Icon=AppBrand.LoadIcon();Text="모두찾아줘 · 자연어 근거 검색";Size=new(1240,900);MinimumSize=new(1000,740);StartPosition=FormStartPosition.CenterParent;question.Text=initialQuestion;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(20)};layout.ColumnStyles.Add(new(SizeType.Percent,100));
        foreach(var h in new[]{52,72})layout.RowStyles.Add(new(SizeType.Absolute,h));layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(new Label{Text="내 PC 문서는 로컬에서 검색합니다. 공공자료를 선택하면 질문을 해당 조회 서비스로 전달합니다.\n원문 발췌가 기본이며, 로컬 AI 요약은 버튼을 눌렀을 때만 실행합니다.",Dock=DockStyle.Fill},0,0);layout.Controls.Add(question,0,1);
        var tools=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Margin=Padding.Empty};scope.Items.AddRange(Domains.Select(d=>d.Name).ToArray());scope.SelectedIndex=0;tools.Controls.AddRange([scope,ask,cancel,settings,ai,export,open]);layout.Controls.Add(tools,0,2);
        status.MinimumSize=new(0,40);status.Margin=Padding.Empty;layout.Controls.Add(status,0,3);
        var split=new SplitContainer{Dock=DockStyle.Fill,Size=new(1100,500),SplitterDistance=360,Panel1MinSize=220,Panel2MinSize=300};
        sources.Columns.Add(new DataGridViewTextBoxColumn{Name="Title",HeaderText="원문 근거",FillWeight=65});sources.Columns.Add(new DataGridViewTextBoxColumn{Name="Locator",HeaderText="쪽·조문·기간",FillWeight=35});sources.RowTemplate.Height=58;sources.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        split.Panel1.Controls.Add(sources);var tabs=new TabControl{Dock=DockStyle.Fill};var tabAnswer=new TabPage("답변");tabAnswer.Controls.Add(answer);var tabOriginal=new TabPage("선택한 원문");tabOriginal.Controls.Add(original);tabs.TabPages.AddRange([tabAnswer,tabOriginal]);split.Panel2.Controls.Add(tabs);layout.Controls.Add(split,0,4);
        var footer=new Label{Text="공시연도·법령 시행일·지원사업 신청기간은 원문 기준입니다. 조회 실패·근거 부족은 답변에 표시합니다.",AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,10,0,0)};layout.Controls.Add(footer,0,5);Controls.Add(layout);
        cancel.Enabled=ai.Enabled=export.Enabled=open.Enabled=false;
        ask.Click+=async(_,_)=>{running=Search();await running;};cancel.Click+=(_,_)=>operation?.Cancel();
        settings.Click+=(_,_)=>{using var dialog=new ResearchSettingsForm();dialog.ShowDialog(this);};ai.Click+=async(_,_)=>{running=Summarize();await running;};export.Click+=(_,_)=>Export();open.Click+=(_,_)=>OpenSource();sources.SelectionChanged+=(_,_)=>ShowSource();
        question.KeyDown+=async(_,e)=>{if(e.Control&&e.KeyCode==Keys.Enter&&ask.Enabled){e.SuppressKeyPress=true;running=Search();await running;}};
        FormClosing+=async(_,e)=>{if(running is {IsCompleted:false}&&!closing){e.Cancel=true;closing=true;operation?.Cancel();try{await running;}catch{}Close();}};
    }
    private void Busy(bool value)
    {
        ask.Enabled=settings.Enabled=scope.Enabled=question.Enabled=!value;cancel.Enabled=value;ai.Enabled=!value&&result?.Evidence.Count>0;export.Enabled=!value&&answer.TextLength>0;
    }
    private async Task Search()
    {
        if(operation is not null)return;operation=new();Busy(true);status.Text="원문 자료를 조회하고 있습니다…";result=null;sources.Rows.Clear();answer.Clear();original.Clear();
        try
        {
            using var service=new ResearchService(store,ResearchSettings.Load());result=await service.AskAsync(question.Text,Domains[scope.SelectedIndex].Domain,operation.Token);
            foreach(var source in result.Evidence){var i=sources.Rows.Add(source.Title,source.Locator);sources.Rows[i].Tag=source;}
            answer.Text=result.Answer;status.Text=$"원문 근거 {result.Evidence.Count}개 · 조회 안내 {result.Notices.Count}개 · 원문 발췌 답변";ShowSource();
        }
        catch(OperationCanceledException){status.Text="조회를 중지했습니다.";}
        catch(Exception ex){status.Text="조회하지 못했습니다. "+ex.Message;}
        finally{operation.Dispose();operation=null;if(!IsDisposed)Busy(false);}
    }
    private async Task Summarize()
    {
        if(operation is not null||result is null)return;operation=new();Busy(true);status.Text="로컬 AI가 확보한 근거를 정리하고 있습니다…";
        try
        {
            var config=ResearchSettings.Load();if(config.LocalModel.Length==0){using var dialog=new ResearchSettingsForm();if(dialog.ShowDialog(this)!=DialogResult.OK)return;config=ResearchSettings.Load();}
            using var local=new LocalResearchAi();answer.Text=await local.SummarizeAsync(result,config.LocalModel,operation.Token);status.Text="로컬 AI 근거 선택 완료 · 선택한 원문을 그대로 발췌했습니다.";
        }
        catch(OperationCanceledException){status.Text="로컬 AI 요약을 중지했습니다. 원문 답변을 유지합니다.";}
        catch(Exception ex){status.Text=ex.Message;}
        finally{operation.Dispose();operation=null;if(!IsDisposed)Busy(false);}
    }
    private void ShowSource()
    {
        var e=sources.CurrentRow?.Tag as Evidence;open.Enabled=e is not null;
        original.Text=e is null?"":e.Title+"\n출처: "+e.Source+"\n위치: "+e.Locator+"\n원문: "+e.Url+"\n조회: "+e.RetrievedAt.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd HH:mm")+"\n\n"+e.Text;
    }
    private void OpenSource()
    {
        if(sources.CurrentRow?.Tag is not Evidence e)return;
        try
        {
            var uri=new Uri(e.Url);if(e.Local){if(!uri.IsFile||!File.Exists(uri.LocalPath))throw new IOException("원본 파일을 찾을 수 없습니다.");}
            else if(uri.Scheme!="https"||!new[]{"law.go.kr","schoolinfo.go.kr","neis.go.kr","kosis.kr","bizinfo.go.kr","k-startup.go.kr","mss.go.kr","fss.or.kr"}.Any(h=>uri.Host==h||uri.Host.EndsWith("."+h)))throw new IOException("공식 원문 주소를 확인하지 못했습니다.");
            Process.Start(new ProcessStartInfo(e.Local?uri.LocalPath:uri.AbsoluteUri){UseShellExecute=true});
        }
        catch(Exception ex){status.Text="원문을 열지 못했습니다. "+ex.Message;}
    }
    private void Export()
    {
        using var dialog=new SaveFileDialog{Title="질문·답변·원문 근거 저장",Filter="Markdown|*.md",FileName="모두찾아줘_근거답변_"+DateTime.Now.ToString("yyyyMMdd_HHmm")+".md"};
        if(dialog.ShowDialog(this)==DialogResult.OK)try{File.WriteAllText(dialog.FileName,answer.Text,new System.Text.UTF8Encoding(false));status.Text="질문·답변·원문 링크를 저장했습니다.";}catch(Exception ex){status.Text=ex.Message;}
    }
}
