using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using STranslate.Log;
using STranslate.Model;
using STranslate.Util;
using System.Collections.ObjectModel; // 添加这行

namespace STranslate.ViewModels.Preference.OCR;

public partial class OpenAIOCR : ObservableObject, IOCR
{
    #region Constructor

    public OpenAIOCR()
        : this(Guid.NewGuid(), "https://api.openai.com", "OpenAIOCR", isEnabled: false)
    {
    }

    public OpenAIOCR(
        Guid guid,
        string url,
        string name = "",
        IconType icon = IconType.OpenAI,
        string appID = "",
        string appKey = "",
        bool isEnabled = true,
        OCRType type = OCRType.OpenAIOCR
    )
    {
        Identify = guid;
        Url = url;
        Name = name;
        Icon = icon;
        AppID = appID;
        AppKey = appKey;
        IsEnabled = isEnabled;
        Type = type;
    }

    #endregion Constructor

    #region Properties

    [ObservableProperty] private Guid _identify = Guid.Empty;

    [JsonIgnore] [ObservableProperty] private OCRType _type = OCRType.BaiduOCR;

    [JsonIgnore][ObservableProperty] private double _temperature = 1.0;

    [JsonIgnore] [ObservableProperty] private bool _isEnabled = true;

    [JsonIgnore] [ObservableProperty] private string _name = string.Empty;

    [JsonIgnore] [ObservableProperty] private IconType _icon = IconType.BaiduBce;

    [JsonIgnore]
    [ObservableProperty]
    [property: DefaultValue("")]
    [property: JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string _url = string.Empty;

    [JsonIgnore]
    [ObservableProperty]
    [property: DefaultValue("")]
    [property: JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string _appID = string.Empty;

    [JsonIgnore]
    [ObservableProperty]
    [property: DefaultValue("")]
    [property: JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string _appKey = string.Empty;

    [JsonIgnore] public Dictionary<IconType, string> Icons { get; private set; } = Constant.IconDict;

    #region Llm Profile

    [JsonIgnore]
    [ObservableProperty]
    [property: DefaultValue("")]
    [property: JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    private string _model = "gpt-4o-2024-08-06";

    /// <summary>
    ///     <see href="https://platform.openai.com/docs/guides/structured-outputs#supported-models"/>
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<string> Models { get; set; } = new()
    {
        "gpt-4o-2024-08-06",
        "gpt-4o-2024-11-20", 
        "claude-3-5-sonnet-20241022",
        "gemini-1.5-pro-latest",
        "gpt-4o-mini-2024-07-18"
    };

    [JsonIgnore]
    [ObservableProperty]
    [property: DefaultValue("")]
    [property: JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    private string _systemPrompt = "You are a specialized OCR engine that accurately extracts each text from the image.";
    
    [JsonIgnore]
    [ObservableProperty]
    [property: DefaultValue("")]
    [property: JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    private string _userPrompt = "Please recognize the text in the picture, the language in the picture is $target";

    [JsonIgnore]
    public bool IsEditable => true;

    #endregion

    #region Show/Hide Encrypt Info

    [JsonIgnore] [ObservableProperty] [property: JsonIgnore]
    private bool _idHide = true;

    [JsonIgnore] [ObservableProperty] [property: JsonIgnore]
    private bool _keyHide = true;

    private void ShowEncryptInfo(string? obj)
    {
        switch (obj)
        {
            case null:
                return;
            case nameof(AppID):
                IdHide = !IdHide;
                break;
            case nameof(AppKey):
                KeyHide = !KeyHide;
                break;
        }
    }

    private RelayCommand<string>? showEncryptInfoCommand;

    [JsonIgnore]
    public IRelayCommand<string> ShowEncryptInfoCommand =>
        showEncryptInfoCommand ??= new RelayCommand<string>(ShowEncryptInfo);

    #endregion Show/Hide Encrypt Info

    #endregion Properties

    #region Interface Implementation

    public async Task<OcrResult> ExecuteAsync(byte[] bytes, LangEnum lang, CancellationToken cancelToken)
    {
        UriBuilder uriBuilder = new(Url);

        // 兼容旧版API: https://platform.openai.com/docs/guides/text-generation
        if (!uriBuilder.Path.EndsWith("/v1/chat/completions"))
            uriBuilder.Path = "/v1/chat/completions";

        #region 构造请求数据
        // 温度限定
        var aTemperature = Math.Clamp(Temperature, 0, 2);

        var openAiModel = Model.Trim();
        var base64Str = Convert.ToBase64String(bytes);
        
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
        {
            messages.Add(new
            {
                role = "system",
                content = SystemPrompt
            });
        }
        messages.Add(new
        {
            role = "user",
            content = new object[]
            {
                new
                {
                    type = "text",
                    text = UserPrompt
                },
                new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = $"data:image/png;base64,{base64Str}"
                    }
                }
            }
        });
        
        var data = new
        {
            model = openAiModel,
            messages = messages.ToArray(),
            temperature = aTemperature,
        };

        #endregion
        
        var jsonData = JsonConvert.SerializeObject(data);
        var target = LangConverter(lang) ?? throw new Exception($"该服务不支持{lang.GetDescription()}");
        jsonData = jsonData.Replace("$target", target);

        var headers = new Dictionary<string, string> { { "Authorization", $"Bearer {AppKey}" } };

        var ocrResult = new OcrResult();
        var resp = await HttpUtil.PostAsync(uriBuilder.Uri.AbsoluteUri, jsonData, null, headers, cancelToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(resp))
            throw new Exception("请求结果为空");

        // 解析AI回答内容
        var content = JsonConvert.DeserializeObject<JObject>(resp)?["choices"]?[0]?["message"]?["content"]?.ToString() ??
                        throw new Exception($"解析失败: {resp}");

        // 将AI回答作为一个OCR结果
        ocrResult.OcrContents.Add(new OcrContent(content));
        
        return ocrResult;
    }

    public IOCR Clone()
    {
        return new OpenAIOCR
        {
            Identify = Identify,
            Type = Type,
            IsEnabled = IsEnabled,
            Icon = Icon,
            Name = Name,
            Url = Url,
            AppID = AppID,
            AppKey = AppKey,
            Icons = Icons,
            Model = Model,
            SystemPrompt = SystemPrompt,
            UserPrompt = UserPrompt
        };
    }

    /// <summary>
    ///     https://zh.wikipedia.org/wiki/ISO_639-1%E4%BB%A3%E7%A0%81%E5%88%97%E8%A1%A8
    /// </summary>
    /// <param name="lang"></param>
    /// <returns></returns>
    public string? LangConverter(LangEnum lang)
    {
        return lang switch
        {
            LangEnum.auto => "auto",
            LangEnum.zh_cn => "Simplified Chinese",
            LangEnum.zh_tw => "Traditional Chinese",
            LangEnum.yue => "Cantonese",
            LangEnum.ja => "Japanese",
            LangEnum.en => "English",
            LangEnum.ko => "Korean",
            LangEnum.fr => "French",
            LangEnum.es => "Spanish",
            LangEnum.ru => "Russian",
            LangEnum.de => "German",
            LangEnum.it => "Italian",
            LangEnum.tr => "Turkish",
            LangEnum.pt_pt => "Portuguese",
            LangEnum.pt_br => "Portuguese",
            LangEnum.vi => "Vietnamese",
            LangEnum.id => "Indonesian",
            LangEnum.th => "Thai",
            LangEnum.ms => "Malay",
            LangEnum.ar => "Arabic",
            LangEnum.hi => "Hindi",
            LangEnum.mn_cy => "Mongolian",
            LangEnum.mn_mo => "Mongolian",
            LangEnum.km => "Central Khmer",
            LangEnum.nb_no => "Norwegian Bokmål",
            LangEnum.nn_no => "Norwegian Nynorsk",
            LangEnum.fa => "Persian",
            LangEnum.sv => "Swedish",
            LangEnum.pl => "Polish",
            LangEnum.nl => "Dutch",
            LangEnum.uk => "Ukrainian",
            _ => "auto"
        };
    }

    #endregion Interface Implementation

    #region Support

    public List<BoxPoint> Converter(Location location)
    {
        return
        [
            //left top
            new BoxPoint(location.left, location.top),

            //right top
            new BoxPoint(location.left + location.width, location.top),

            //right bottom
            new BoxPoint(location.left + location.width, location.top + location.height),

            //left bottom
            new BoxPoint(location.left, location.top + location.height)
        ];
    }

    public class Location
    {
        /// <summary>
        /// </summary>
        public int top { get; set; }

        /// <summary>
        /// </summary>
        public int left { get; set; }

        /// <summary>
        /// </summary>
        public int width { get; set; }

        /// <summary>
        /// </summary>
        public int height { get; set; }
    }

    public class Words_resultItem
    {
        /// <summary>
        /// </summary>
        public string words { get; set; } = string.Empty;

        /// <summary>
        /// </summary>
        public Location location { get; set; } = new();
    }

    public class Root
    {
        /// <summary>
        /// </summary>
        public List<Words_resultItem> words_result { get; set; } = [];
    }

    #endregion Support
}