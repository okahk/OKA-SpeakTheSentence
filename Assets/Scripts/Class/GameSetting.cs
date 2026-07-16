using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;

public class GameSetting : MonoBehaviour
{
    public HostName currentHostName = HostName.dev;
    private string _currentHostName;
    public string currentURL;
    public GameSetup gameSetup;
    public APIManager apiManager;
    public delegate void ParameterHandler(string value);
    protected private Dictionary<string, ParameterHandler> customHandlers = new Dictionary<string, ParameterHandler>();
    public string unitKey = string.Empty;
    public string testURL = string.Empty;
    public bool skipAudioPanel = false;

    protected virtual void Awake()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        Application.runInBackground = true;
        DontDestroyOnLoad(this);
        this.CheckDeviceGradeAndSetQuality();
    }

    private void CheckDeviceGradeAndSetQuality()
    {
        // Example thresholds for low-grade devices
        //int lowGradeMaxMemory = 2048; // 2GB RAM
        //int lowGradeMaxShaderLevel = 30; // Shader Model 3.0
        //int lowGradeMaxProcessorCount = 4; // Quad-core CPU
        // Check device specifications
        int systemMemorySize = SystemInfo.systemMemorySize; // RAM in MB
        int shaderLevel = SystemInfo.graphicsShaderLevel; // Shader model
        int processorCount = SystemInfo.processorCount; // Number of CPU cores
        string deviceModel = SystemInfo.deviceModel;
        string gpuName = SystemInfo.graphicsDeviceName;
        int gpuMemory = SystemInfo.graphicsMemorySize; // In MB


        Debug.Log($"Device Info: Model={deviceModel}, RAM={systemMemorySize}MB, ShaderLevel={shaderLevel}, CPU Cores={processorCount};GPU Info: Name={gpuName}, Memory={gpuMemory}MB");

        // Determine if the device is low-grade
        /*if (systemMemorySize <= lowGradeMaxMemory || shaderLevel <= lowGradeMaxShaderLevel || processorCount <= lowGradeMaxProcessorCount)
        {
            Debug.Log("Low-grade device detected. Reducing quality settings.");
            this.ReduceQualitySettings();
        }
        else
        {
            Debug.Log("Device is not low-grade. Keeping default quality settings.");
        }*/

        if (gpuMemory < 512) // Example threshold: less than 512MB GPU memory
        {
            Debug.Log("Low-grade device detected based on GPU performance.");
            this.ReduceQualitySettings();
        }
        else
        {
            Debug.Log("GPU is sufficient. Keeping default quality settings.");
        }
    }

    private void ReduceQualitySettings()
    {
        QualitySettings.SetQualityLevel(0, true);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 30;
        Debug.Log("Quality settings reduced for low-grade device.");
    }

    public string GetCurrentDomainName(string Url = "")
    {
        if (string.IsNullOrEmpty(Url))
            return null;
        else
        {
            Uri url = new Uri(Url);
            return url.Host;
        }
    }

    protected virtual void GetParseURLParams()
    {
        this.CurrentURL = string.IsNullOrEmpty(Application.absoluteURL) ? this.testURL : Application.absoluteURL;
        LogController.Instance?.debug("Current URL: " + this.CurrentURL);
        string hostName = this.GetCurrentDomainName(this.CurrentURL);
        LogController.Instance?.debug("Current hostName: " + hostName);
        switch (hostName)
        {
            case "dev.openknowledge.hk":
            case "devapp.openknowledge.hk":
            case "dev.starwishparty.com":
                this.currentHostName = HostName.dev;
                LogController.Instance?.UpdateVersion("dev");
                break;
            case "uat.openknowledge.hk":
            case "uat.starwishparty.com":
                this.currentHostName = HostName.uat;
                LogController.Instance?.UpdateVersion("uat");
                break;
            case "pre.openknowledge.hk":
            case "pre.starwishparty.com":
                this.currentHostName = HostName.preprod;
                LogController.Instance?.UpdateVersion("preprod");
                break;
            case "www.rainbowone.app":
            case "rainbowone.app":
            case "api.openknowledge.hk":
            case "www.starwishparty.com":
            case "starwishparty.com":
            case "app.starwishparty.com":
                this.currentHostName = HostName.prod;
                LogController.Instance?.UpdateVersion("prod");
                break;
            default:
                if (string.IsNullOrEmpty(hostName))
                {
                    this.currentHostName = HostName.dev;
                    LogController.Instance?.UpdateVersion("dev");
                }
                else
                {
                    if (hostName.Contains("dev"))
                    {
                        this.currentHostName = HostName.dev;
                        LogController.Instance?.UpdateVersion("dev");
                    }
                    else if (hostName.Contains("uat"))
                    {
                        this.currentHostName = HostName.uat;
                        LogController.Instance?.UpdateVersion("uat");
                    }
                    else if (hostName.Contains("pre"))
                    {
                        this.currentHostName = HostName.preprod;
                        LogController.Instance?.UpdateVersion("preprod");
                    }
                    else
                    {
                        this.currentHostName = HostName.prod;
                        LogController.Instance?.UpdateVersion("prod");
                    }
                }
                break;
        }
        this.CurrentHostName = hostName;

        string[] urlParts = this.CurrentURL.Split('?');
        if (urlParts.Length > 1)
        {
            string queryString = urlParts[1];
            string[] parameters = queryString.Split('&');

            foreach (string parameter in parameters)
            {
                string[] keyValue = parameter.Split('=');
                if (keyValue.Length == 2)
                {
                    string key = keyValue[0];
                    string value = keyValue[1];
                    LogController.Instance?.debug($"Parameter Key: {key}, Value: {value}");

                    if (!string.IsNullOrEmpty(value))
                    {

                        switch (key)
                        {
                            case "jwt":
                                this.apiManager.jwt = value;
                                LogController.Instance?.debug("Current jwt: " + this.apiManager.jwt);
                                break;
                            case "id":
                                this.apiManager.appId = value;
                                LogController.Instance?.debug("Current app/book id: " + this.apiManager.appId);
                                break;
                            case "unit":
                                this.unitKey = value;
                                LogController.Instance?.debug("Current Game Unit: " + this.unitKey);
                                break;
                            case "gameTime":
                                this.GameTime = float.Parse(value);
                                LogController.Instance?.debug("Game Time: " + this.GameTime);
                                this.ShowFPS = true;
                                break;
                            case "playerNumbers":
                                this.PlayerNumbers = int.Parse(value);
                                LogController.Instance?.debug("player Numbers: " + this.PlayerNumbers);
                                break;
                            case "lang":
                                if (value == "tc" || value == "sc")
                                {
                                    this.Lang = 1;
                                }
                                else if (value == "en")
                                {
                                    this.Lang = 0;
                                }
                                else
                                {
                                    this.Lang = int.Parse(value);
                                }
                                LogController.Instance?.debug("Current Language: " + this.Lang);
                                break;
                            case "returnUrl":
                                this.ReturnUrl = UnityWebRequest.UnEscapeURL(value);
                                LogController.Instance?.debug("ReturnUrl: " + this.ReturnUrl);
                                ExternalCaller.RemoveReturnUrlFromAddressBar();
                                break;
                            default:
                                if (this.customHandlers.TryGetValue(key, out ParameterHandler handler))
                                {
                                    handler(value);
                                }
                                break;
                        }
                    }
                }
            }
        }
    }

    public void RegisterCustomHandler(string key, ParameterHandler handler)
    {
        if (!this.customHandlers.ContainsKey(key))
        {
            this.customHandlers[key] = handler;
        }
    }

    protected virtual void Start()
    {
        this.apiManager.Init();
    }

    protected virtual void Update()
    {
        this.apiManager.controlDebugLayer();
    }

    public void InitialGameImages(Action onCompleted = null)
    {
        if (this.apiManager.IsLogined)
        {
            this.initialGameImagesByAPI(onCompleted);
        }
        else
        {
            this.initialGameImagesByLocal(onCompleted);
        }
    }

    private void initialGameImagesByLocal(Action onCompleted = null)
    {
        //Download game background image from local streaming assets
        this.gameSetup.loadImageMethod = LoadImageMethod.StreamingAssets;
        StartCoroutine(this.gameSetup.Load("GameUI", "bg", _bgTexture =>
        {
            LogController.Instance?.debug($"Downloaded bg Image!!");
            if(_bgTexture != null) this.gameSetup.bgTexture = _bgTexture;

            StartCoroutine(this.gameSetup.Load("GameUI", "preview", _previewTexture =>
            {
                LogController.Instance?.debug($"Downloaded preview Image!!");
                ExternalCaller.UpdateLoadBarStatus("Loaded UI");
                if(_previewTexture != null) this.gameSetup.previewTexture = _previewTexture;
                onCompleted?.Invoke();
            }));
        }));
    }

    private void initialGameImagesByAPI(Action onCompleted = null)
    {
        //Download game background image from api
        this.gameSetup.loadImageMethod = LoadImageMethod.Url;
        var imageUrls = new List<string>
        {
            this.apiManager.settings.backgroundImageUrl,
            this.apiManager.settings.previewGameImageUrl,
        };
        imageUrls = imageUrls.Where(url => !string.IsNullOrEmpty(url)).ToList();

        if (imageUrls.Count > 0)
        {
            StartCoroutine(LoadImages(imageUrls, onCompleted));
        }
        else
        {
            LogController.Instance?.debug($"No valid image URLs found!!");
            onCompleted?.Invoke();
        }
    }

    private IEnumerator LoadImages(List<string> imageUrls, Action onCompleted)
    {
        foreach (var url in imageUrls)
        {
            Texture texture = null;
            // Load each image
            yield return StartCoroutine(this.gameSetup.Load("", url, _texture =>
            {
                texture = _texture;
                LogController.Instance?.debug($"Downloaded image from: {url}");
                ExternalCaller.UpdateLoadBarStatus($"Loaded UI");
            }));

            // Assign textures based on their URL
            if (url == this.apiManager.settings.backgroundImageUrl)
            {
                this.gameSetup.bgTexture = texture != null ? texture : null;
            }
            else if (url == this.apiManager.settings.previewGameImageUrl)
            {
                this.gameSetup.previewTexture = texture != null ? texture : null;
            }
        }

        onCompleted?.Invoke();
    }

    public void InitialGameSetup()
    {
        if (this.apiManager.IsLogined && !this.apiManager.IsLoginedRainbowOne) this.Lang = 0;
        this.gameSetup.setBackground();
        this.gameSetup.setInstruction(this.apiManager.settings.instructionContent);
    }
    public string CurrentURL
    {
        set { this.currentURL = value; }
        get { return this.currentURL; }
    }

    public float GameTime
    {
        get { return this.gameSetup.gameTime; }
        set { this.gameSetup.gameTime = value; }
    }

    public bool ShowFPS
    {
        get { return this.gameSetup.showFPS; }
        set { this.gameSetup.showFPS = value; }
    }

    public int PlayerNumbers
    {
        get { return this.gameSetup.playerNumber; }
        set { this.gameSetup.playerNumber = value; }
    }

    public int Lang
    {
        get { return this.gameSetup.lang; }
        set { this.gameSetup.lang = value; }
    }

    public string ReturnUrl
    {
        get { return this.gameSetup.returnUrl; }
        set { this.gameSetup.returnUrl = value; }
    }


    public string RoAppDataKey
    {
        get { return this.gameSetup.roAppDataKey; }
        set { this.gameSetup.roAppDataKey = value; }
    }


    public string CurrentHostName
    {
        set
        {
            this._currentHostName = value;
        }
        get
        {
            if(string.IsNullOrEmpty(this._currentHostName)) 
                return "dev.openknowledge.hk";
            else
                return this._currentHostName;
        }
    }

    public void Reload()
    {
        ExternalCaller.ReLoadCurrentPage();
    }

    public void changeScene(int sceneId)
    {
        SceneManager.LoadScene(sceneId);
    }
}

[Serializable]
public class GameSetup : LoadImage
{
    [Tooltip("RainbowOne book question dataKey")]
    public string roAppDataKey = string.Empty;
    [Tooltip("Game Page Name")]
    public string gamePageName = "";
    [Tooltip("Default Game Background Texture")]
    public Texture bgTexture;
    [Tooltip("Default Game Preview Texture")]
    public Texture previewTexture;
    [Tooltip("Find Tag name of GameBackground in different scene")]
    public RawImage gameBackground;
    [Tooltip("Instruction Preview Image")]
    public RawImage gamePreview;
    [Tooltip("Game Exit Method, 0 is for demo page; 1 is back to roWeb; 2 is restart game")]
    public int gameExitType = 0;
    public InstructionText instructions;
    public float gameTime;
    public Inventory inventory;
    public int helpItemTypeOfId = 3;
    public int retry_times = 3;
    public string returnUrl = "";
    public bool showFPS = false;
    public int playerNumber = 1;
    public int qa_font_alignment = 1; // 0: left, 1: center, 2: right
    public int passAccuracyScore = 60;
    public int passPronScore = 60;
    [Range(0, 1)]
    public int lang = 0;
    public int gameSettingScore = -1;
    public int gameTotalStars = 3;

    public void setBackground()
    {
        if (this.gameBackground == null)
        {
            var tex = GameObject.FindGameObjectWithTag("GameBackground");
            this.gameBackground = tex.GetComponent<RawImage>();
        }

        if (this.gameBackground != null)
        {
            this.gameBackground.texture = this.bgTexture;
        }
    }

    public void setInstruction(string content = "")
    {
        if (!string.IsNullOrEmpty(content) && this.instructions == null)
        {
            var instructionText = GameObject.FindGameObjectWithTag("Instruction");
            this.instructions = instructionText != null ? instructionText.GetComponent<InstructionText>() : null;
            if (instructionText != null) this.instructions.setContent(content);
        }

        if (this.gamePreview == null)
        {
            var preview = GameObject.FindGameObjectWithTag("GamePreview");
            
            if (preview != null) {
                var aspectRatio = preview.GetComponent<AspectRatioFitter>();
                this.gamePreview = preview.GetComponent<RawImage>();

                if(this.gamePreview != null) this.gamePreview.texture = this.previewTexture;

                if(aspectRatio != null)
                {
                    aspectRatio.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
                    aspectRatio.aspectRatio = (float)this.previewTexture.width / this.previewTexture.height;
                }
            }
        }
    }
}

[Serializable]
public class StarwishPartyAccountResponse
{
    public string status;
    public AccountData data;
}

[Serializable]
public class AccountData
{
    public string id;
    public string currency;
    public string equipped_costume;
    public string[] owned_costume;
    public string created_at;
    public string updated_at;
    public object deleted_at; // Use 'object' for nullable fields
    public EquippedCostumeData equipped_costume_data;
}

[Serializable]
public class EquippedCostumeData
{
    public string costume_id;
    public string costume_name;
    public string description;
    public string price;
    public string img_src_wholebody;
    public string img_src_head;
    public string created_at;
    public string updated_at;
    public object deleted_at; // Use 'object' for nullable fields
}

[Serializable]
public class HelpToolInventory
{
    public int help_tool_id;
    public string help_tool_name;
    public string description;
    public int amount;
}

[Serializable]
public class Inventory
{
    public string status;
    public HelpToolInventory[] data;
}

[Serializable]
public class HelpToolRequest
{
    public int help_tool_id;
    public int amount;
}


public enum HostName
{
    dev,
    uat,
    preprod,
    prod
}