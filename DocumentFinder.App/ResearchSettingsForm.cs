using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentFinder.Core;

namespace DocumentFinder.App;

internal static class ResearchSettings
{
    private sealed record Stored(string Law,string Dart,string Bizinfo,string Model);
    private static string PathName=>Path.Combine(Program.DataDirectory,"research-connections.json");
    private static string Protect(string s)=>s.Length==0?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s),null,DataProtectionScope.CurrentUser));
    private static string Unprotect(string s){try{return s.Length==0?"":Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s),null,DataProtectionScope.CurrentUser));}catch(CryptographicException){return "";}catch(FormatException){return "";}}
    public static ResearchConfiguration Load()
    {
        try
        {
            var s=JsonSerializer.Deserialize<Stored>(File.ReadAllText(PathName));
            if(s is not null)return new(Unprotect(s.Law),Unprotect(s.Dart),Unprotect(s.Bizinfo),s.Model);
        }
        catch(Exception ex) when(ex is IOException or JsonException){}
        return new(Environment.GetEnvironmentVariable("LAW_OC")??"",Environment.GetEnvironmentVariable("DART_API_KEY")??"",Environment.GetEnvironmentVariable("BIZINFO_API_KEY")??"");
    }
    public static void Save(ResearchConfiguration c)
    {
        Directory.CreateDirectory(Program.DataDirectory);File.WriteAllText(PathName,JsonSerializer.Serialize(new Stored(Protect(c.LawKey),Protect(c.DartKey),Protect(c.BizinfoKey),c.LocalModel)));
    }
}

internal sealed class ResearchSettingsForm:Form
{
    private readonly TextBox law=new(){UseSystemPasswordChar=true,Dock=DockStyle.Fill},dart=new(){UseSystemPasswordChar=true,Dock=DockStyle.Fill},bizinfo=new(){UseSystemPasswordChar=true,Dock=DockStyle.Fill};
    private readonly ComboBox model=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly Label state=new(){AutoSize=true,Dock=DockStyle.Fill};
    public ResearchSettingsForm()
    {
        AutoScaleDimensions=new(96,96);AutoScaleMode=AutoScaleMode.Dpi;Font=new("맑은 고딕",10);Icon=AppBrand.LoadIcon();Text="근거 검색 · 연결 설정";Size=new(820,560);MinimumSize=new(720,520);StartPosition=FormStartPosition.CenterParent;
        var c=ResearchSettings.Load();law.Text=c.LawKey;dart.Text=c.DartKey;bizinfo.Text=c.BizinfoKey;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=2,RowCount=8};layout.ColumnStyles.Add(new(SizeType.Absolute,220));layout.ColumnStyles.Add(new(SizeType.Percent,100));
        layout.RowStyles.Add(new(SizeType.Absolute,70));for(var i=0;i<4;i++)layout.RowStyles.Add(new(SizeType.Absolute,48));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,54));layout.RowStyles.Add(new(SizeType.AutoSize));
        var intro=new Label{Text="원문 발췌 답변은 AI 없이 동작합니다. 공공 조회는 인터넷을 사용합니다.\n인증키는 현재 Windows 사용자 계정으로 암호화하여 이 PC에 저장합니다.",Dock=DockStyle.Fill};layout.Controls.Add(intro,0,0);layout.SetColumnSpan(intro,2);
        void Row(string name,Control input,int row){layout.Controls.Add(new Label{Text=name,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);input.AccessibleName=name;layout.Controls.Add(input,1,row);}
        Row("법제처 OC (선택)",law,1);Row("OpenDART 인증키",dart,2);Row("기업마당 인증키 (선택)",bizinfo,3);Row("Ollama 로컬 모델",model,4);
        model.Items.Add("사용 안 함");model.SelectedIndex=0;
        var links=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};
        foreach(var (title,url) in new[]{("법제처 키 발급","https://open.law.go.kr/"),("OpenDART 키 발급","https://opendart.fss.or.kr/"),("기업마당 API","https://www.bizinfo.go.kr/apiList.do"),("Ollama 설치 안내","https://ollama.com/download/windows")})
        {var link=new LinkLabel{Text=title,AutoSize=true,Margin=new Padding(0,10,20,10)};link.LinkClicked+=(_,_)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});links.Controls.Add(link);}
        layout.Controls.Add(links,0,5);layout.SetColumnSpan(links,2);layout.Controls.Add(state,0,6);layout.SetColumnSpan(state,2);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft};var save=new Button{Text="설정 저장",AutoSize=true,MinimumSize=new(100,36)};var close=new Button{Text="취소",AutoSize=true,MinimumSize=new(85,36)};
        save.Click+=(_,_)=>{ResearchSettings.Save(new(law.Text.Trim(),dart.Text.Trim(),bizinfo.Text.Trim(),model.SelectedIndex>0?model.Text:""));DialogResult=DialogResult.OK;Close();};close.Click+=(_,_)=>Close();buttons.Controls.AddRange([close,save]);layout.Controls.Add(buttons,0,7);layout.SetColumnSpan(buttons,2);Controls.Add(layout);
        Shown+=async(_,_)=>{try{using var ai=new LocalResearchAi();var models=await ai.ModelsAsync();if(IsDisposed)return;model.Items.AddRange(models);var preferred=models.FirstOrDefault(m=>m==c.LocalModel)??models.FirstOrDefault(m=>m=="gemma4:latest")??models.FirstOrDefault();if(preferred is not null)model.SelectedItem=preferred;state.Text="로컬 AI 연결됨 · http://127.0.0.1:11434 · 선택한 자료를 이 PC에서만 처리합니다.";}catch(Exception){if(!IsDisposed)state.Text="Ollama가 실행되어 있지 않습니다. 원문 발췌 답변은 그대로 사용할 수 있습니다.";}};
    }
}
