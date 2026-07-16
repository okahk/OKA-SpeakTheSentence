using SimpleJSON;
using System;
using System.Collections;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

[Serializable]
public class APIManager
{
    [Tooltip("Starwish party supported domains")]
    public string[] starwishPartyDomains;
    [Tooltip("Account Jwt token, upload data jwt")]
    public string jwt;
    [Tooltip("Created App/Book current id")]
    public string appId;
    [Tooltip("Game Setting Json")]
    public string gameSettingJson = string.Empty;
    [Tooltip("Question Json")]
    public string questionJson = string.Empty;
    [Tooltip("Account Json")]
    public string accountJson = string.Empty;
    [Tooltip("Simple Account id")]
    public int accountId = -1;
    [Tooltip("Role Account uid")]
    public int accountUid = -1;
    [Tooltip("Account Icon Image Url")]
    public string photoDataUrl = string.Empty;
    [Tooltip("Payloads Object for data send out")]
    public string payloads = string.Empty;
    [SerializeField]
    private bool isLogined = false;
    [SerializeField]
    private bool isRainbowAppLogined = false;
    [SerializeField]
    private bool isShowLoginErrorBox = false;
    [SerializeField]
    private bool showingDebugBox = false;
    public Texture peopleIcon, peopleFullBodyIcon;
    public string loginName = string.Empty;
    public GameSettings settings;
    public int maxRetries = 10;
    public CanvasGroup debugLayer;
    public CanvasGroup loginErrorBox;
    public TextMeshProUGUI loginErrorMessage;
    private Text debugText = null;
    private string errorMessage = "";
    public Answer answer;


    public void Init()
    {
        if (this.debugLayer != null)
        {
            this.debugText = this.debugLayer.GetComponentInChildren<Text>();
        }
        this.resetLoginErrorBox();
    }

    public void controlDebugLayer()
    {
        if (this.debugLayer != null && Input.GetKeyDown("d"))
        {
            showingDebugBox = !showingDebugBox;
            SetUI.Set(this.debugLayer.GetComponent<CanvasGroup>(), showingDebugBox, 0f);
        }
        this.checkLoginErrorBox();
    }

    public bool IsLogined
    {
        set { this.isLogined = value; }
        get { return this.isLogined; }
    }

    public bool IsLoginedRainbowOne
    {
        set { this.isRainbowAppLogined = value; }
        get { return this.isRainbowAppLogined; }
    }

    public bool IsShowLoginErrorBox
    {
        set { this.isShowLoginErrorBox = value; }
        get { return this.isShowLoginErrorBox; }
    }

    public void resetLoginErrorBox()
    {
        this.IsShowLoginErrorBox = false;
        SetUI.Set(this.loginErrorBox, false, 0f);
    }

    public void checkLoginErrorBox()
    {
        SetUI.Set(this.loginErrorBox, this.IsShowLoginErrorBox, 0f);
    }

    private void HandleError(string message, Action onCompleted, bool showErrorBox = false)
    {
        this.errorMessage = message;
        LogController.Instance?.debug(this.errorMessage);
        this.IsShowLoginErrorBox = showErrorBox;
        onCompleted?.Invoke();
    }

    public void PostGameSetting(Action getParseURLParams = null,
                               Action getDataFromRoWebAPI = null,
                               Action getDataFromRoAppAPI = null,
                               Action onCompleted = null)
    {
        getParseURLParams?.Invoke();

        if (!string.IsNullOrEmpty(this.appId) && !string.IsNullOrEmpty(this.jwt))
        {
            this.IsLogined = true;
            getDataFromRoWebAPI?.Invoke();
        }
        else
        {
            if (!string.IsNullOrEmpty(this.jwt) && !string.IsNullOrEmpty(LoaderConfig.Instance.RoAppDataKey))
            {
                LogController.Instance.debug("Logined in RainbowOne App.");
                this.IsLoginedRainbowOne = true;
                getDataFromRoAppAPI?.Invoke();
            }
            else
            {
                this.IsLogined = false;
                this.IsLoginedRainbowOne = false;
                this.HandleError("Missing JWT and App ID.", onCompleted);
            }
        }
    }


    public IEnumerator postGameSetting(Action onCompleted = null)
    {
        string api = APIConstant.GameDataAPI(LoaderConfig.Instance, this.appId, this.jwt);
        LogController.Instance?.debug("called login api: " + api);
        WWWForm form = new WWWForm();
        int retryCount = 0;
        bool requestSuccessful = false;

        while (retryCount < this.maxRetries && !requestSuccessful)
        {
            using (UnityWebRequest www = UnityWebRequest.Post(api, form))
            {
                // Set headers
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("typ", "jwt");
                www.SetRequestHeader("alg", "HS256");
                www.certificateHandler = new WebRequestSkipCert();
                // Send the request and wait for a response
                yield return www.SendWebRequest();

                // Check for errors
                if (www.result != UnityWebRequest.Result.Success)
                {
                    retryCount++;
                    this.HandleError("Error: " + www.error + "Retrying..." + retryCount, null, true);
                    yield return new WaitForSeconds(2);
                }
                else
                {
                    requestSuccessful = true;
                    string responseText = www.downloadHandler.text;
                    int jsonStartIndex = responseText.IndexOf("{\"questions\":");
                    //LogController.Instance?.debug("www.downloadHandler.text: " + responseText);

                    if (jsonStartIndex != -1)
                    {
                        string jsonData = responseText.Substring(jsonStartIndex);
                        var jsonNode = JSONNode.Parse(jsonData);
                        //LogController.Instance?.debug("jsonNode: " + jsonNode.ToString());

                        this.questionJson = jsonNode[APIConstant.QuestionDataHeaderName].ToString(); // Question json data;
                        this.accountJson = jsonNode["account"].ToString(); // Account json data;

                        string accountIdString = jsonNode["account"]["id"];
                        if (!string.IsNullOrEmpty(accountIdString))
                        {
                            int accountId = int.Parse(accountIdString);
                            this.accountId = accountId;
                        }
                        string accountUidString = jsonNode["account"]["uid"];
                        if (!string.IsNullOrEmpty(accountUidString))
                        {
                            int accountUid = int.Parse(accountUidString);
                            this.accountUid = accountUid;
                        }

                        if (jsonNode["photo"] != null) this.photoDataUrl = jsonNode["photo"].ToString(); // Account json data;
                        if (jsonNode["setting"] != null) this.gameSettingJson = jsonNode["setting"].ToString();
                        LogController.Instance?.debug("gameSettingJson: " + this.gameSettingJson);
                        if (jsonNode["payloads"] != null) this.payloads = jsonNode["payloads"].ToString();
                        if (jsonNode["title"] != null) LoaderConfig.Instance.gameSetup.gamePageName = jsonNode["title"].Value;
                        LogController.Instance?.debug("payloads: " + this.payloads);

                        if (!string.IsNullOrEmpty(this.gameSettingJson) && this.gameSettingJson != "{}")
                        {
                            this.settings.gameTime = jsonNode["setting"]["game_time"] != null ? jsonNode["setting"]["game_time"] : null;
                            string bgImagUrl = jsonNode["setting"]["background_image_url"] != null ?
                                jsonNode["setting"]["background_image_url"].ToString().Replace("\"", "") : null;
                            string gamePreviewUrl = jsonNode["setting"]["game_preview_image"] != null ?
                                jsonNode["setting"]["game_preview_image"].ToString().Replace("\"", "") : null;
                            this.settings.instructionContent = jsonNode["setting"]["hint"] != null ?
                                jsonNode["setting"]["hint"].ToString().Replace("\"", "") : null;

                            LoaderConfig.Instance.gameSetup.gameTime = this.settings.gameTime;

                            if (bgImagUrl != null)
                            {
                                if (!bgImagUrl.StartsWith("https://") || !bgImagUrl.StartsWith(APIConstant.blobServerRelativePath))
                                    this.settings.backgroundImageUrl = APIConstant.blobServerRelativePath + bgImagUrl;
                            }
                            if (gamePreviewUrl != null)
                            {
                                if (!gamePreviewUrl.StartsWith("https://") || !gamePreviewUrl.StartsWith(APIConstant.blobServerRelativePath))
                                    this.settings.previewGameImageUrl = APIConstant.blobServerRelativePath + gamePreviewUrl;
                            }
                            ////////Game Customization params/////////
                            SetParams.setCustomParameters(this.settings, jsonNode);
                        }

                        if (this.debugText != null)
                        {
                            this.debugText.text += "Question Data: " + this.questionJson + "\n\n ";
                            this.debugText.text += "Account: " + this.accountJson + "\n\n ";
                            this.debugText.text += "Photo: " + this.photoDataUrl + "\n\n ";
                            this.debugText.text += "Setting: " + this.gameSettingJson + "\n\n ";
                            this.debugText.text += "PayLoad: " + this.payloads + "\n\n ";
                            this.debugText.text += "Is Logined: " + this.IsLogined;
                        }

                        if (!string.IsNullOrEmpty(this.photoDataUrl) && this.photoDataUrl != "null")
                        {
                            string modifiedPhotoDataUrl = photoDataUrl.Replace("\"", "");

                            string imageUrl = modifiedPhotoDataUrl;
                            if (!modifiedPhotoDataUrl.StartsWith("https://"))
                            {
                                imageUrl = "https:" + modifiedPhotoDataUrl;
                            }
                            //LogController.Instance?.debug($"Downloading People Icon!!{imageUrl}");
                            yield return LoaderConfig.Instance.gameSetup.Load("", imageUrl, _peopleIcon =>
                            {
                                LogController.Instance?.debug($"Downloaded People Icon!!");
                                this.peopleIcon = _peopleIcon;
                            });
                        }

                        if (jsonNode["account"] != null && !string.IsNullOrEmpty(this.accountJson))
                        {
                            var name = jsonNode["account"]["display_name"].ToString();
                            if (!string.IsNullOrWhiteSpace(name) && name != "null" && name != null)
                            {
                                this.loginName = name.Replace("\"", "");
                                //LogController.Instance?.debug("Display name: " + this.loginName);
                            }
                            else
                            {
                                //LogController.Instance?.debug("Display name is empty. use first name and last name");
                                var first_name = jsonNode["account"]["first_name"].ToString().Replace("\"", "");
                                var last_name = jsonNode["account"]["last_name"].ToString().Replace("\"", "");
                                this.loginName = last_name;
                                //this.loginName = last_name + " " + first_name;
                            }
                        }

                        //E.g
                        //Debug.Log(jsonNode["account"]["display_name"].ToString());
                        var loader = LoaderConfig.Instance;
                        if (APIConstant.isLoginedStarwishPartySite(loader))
                        {
                            yield return this.GetStarwishAccountData(() =>
                            {
                                LogController.Instance?.debug(this.questionJson);
                                ExternalCaller.UpdateLoadBarStatus("Loaded AC");
                                onCompleted?.Invoke();
                            });
                        }
                        else
                        {
                            ExternalCaller.UpdateLoadBarStatus("Loaded Data");
                            LogController.Instance?.debug(this.questionJson);
                            onCompleted?.Invoke();
                        }
                    }
                    else
                    {
                        this.HandleError("wrong json start index.", onCompleted, true);
                    }
                }
            }
        }

        if (!requestSuccessful)
        {
            this.errorMessage = "Failed to get a successful response after " + maxRetries + " attempts.";
            LogController.Instance?.debug(this.errorMessage);
            this.IsShowLoginErrorBox = true;
            onCompleted?.Invoke();
        }
    }

    private IEnumerator GetStarwishAccountData(Action onCompleted = null)
    {
        string api = APIConstant.GetStarwishPartyAccountAPI(LoaderConfig.Instance);
        LogController.Instance?.debug("called starwish account api: " + api);

        if (string.IsNullOrEmpty(api))
        {
            LogController.Instance.debug("Current site not support starwish account api.");
            yield break;
        }

        int retryCount = 0;
        bool requestSuccessful = false;

        while (retryCount < this.maxRetries && !requestSuccessful)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(api))
            {
                request.SetRequestHeader("accept", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + LoaderConfig.Instance.apiManager.jwt);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    LogController.Instance.debugError($"Attempt {retryCount + 1} failed: {request.error}");
                    retryCount++;

                    if (retryCount >= this.maxRetries)
                    {
                        this.HandleError("Error loading Starwish account API after retries.", onCompleted, true);
                        yield break;
                    }

                    yield return new WaitForSeconds(1f); // Optional delay before retry
                }
                else
                {
                    requestSuccessful = true;

                    string jsonResponse = request.downloadHandler.text;
                    LogController.Instance.debug("Starwish Account Response: " + jsonResponse);
                    StarwishPartyAccountResponse swaResponse = JsonUtility.FromJson<StarwishPartyAccountResponse>(jsonResponse);

                    if (swaResponse != null)
                    {
                        string[] photoUrls = new string[]
                        {
                        swaResponse.data.equipped_costume_data.img_src_wholebody,
                        swaResponse.data.equipped_costume_data.img_src_head
                        };

                        foreach (string url in photoUrls)
                        {
                            if (!string.IsNullOrEmpty(url) && url != "null")
                            {
                                string originalUrl = url.Replace("\"", "").Trim();
                                // Ensure scheme
                                string absoluteUrl = originalUrl;
                                if (absoluteUrl.StartsWith("//"))
                                {
                                    absoluteUrl = "https:" + absoluteUrl;
                                }
                                else if (!absoluteUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                {
                                    absoluteUrl = "https:" + absoluteUrl;
                                }

                                string urlToLoad = absoluteUrl;
                                int qIndex = absoluteUrl.IndexOf('?');
                                string basePart = qIndex >= 0 ? absoluteUrl.Substring(0, qIndex) : absoluteUrl;
                                string query = qIndex >= 0 ? absoluteUrl.Substring(qIndex) : "";

                                if (basePart.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                                {
                                    urlToLoad = basePart.Substring(0, basePart.LastIndexOf('.')) + ".png" + query;
                                    LogController.Instance?.debug($"SVG detected for '{originalUrl}', switching to PNG url '{urlToLoad}'");
                                }

                                yield return LoaderConfig.Instance.gameSetup.Load("", urlToLoad, loadedImage =>
                                {
                                    LogController.Instance?.debug($"Downloaded Image from URL: {urlToLoad}");
                                    if (originalUrl == swaResponse.data.equipped_costume_data.img_src_wholebody)
                                    {
                                        this.peopleFullBodyIcon = loadedImage;
                                    }
                                    else if (originalUrl == swaResponse.data.equipped_costume_data.img_src_head)
                                    {
                                        this.peopleIcon = loadedImage;
                                    }
                                });
                            }
                        }

                        LogController.Instance.debug("Successfully downloaded Starwish account icon images.");
                        onCompleted?.Invoke();
                    }
                }
            }
        }
    }

    public IEnumerator getHelpToolInventory(Action onCompleted = null)
    {
        string api = APIConstant.GetHelpToolInventoryAPI(LoaderConfig.Instance);
        LogController.Instance?.debug("called helpTool Inventory api: " + api);

        if (string.IsNullOrEmpty(api))
        {
            LogController.Instance.debug("Current site not support inventory api.");
            onCompleted?.Invoke();
            yield break;
        }

        int retryCount = 0;
        bool requestSuccessful = false;

        while (retryCount < this.maxRetries && !requestSuccessful)
        {
            UnityWebRequest request = UnityWebRequest.Get(api);
            request.SetRequestHeader("accept", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + LoaderConfig.Instance.apiManager.jwt);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                requestSuccessful = true;

                string json = request.downloadHandler.text;
                LogController.Instance.debug("Inventory Response: " + json);
                Inventory inventoryData = JsonUtility.FromJson<Inventory>(json);
                LoaderConfig.Instance.gameSetup.inventory = inventoryData;
                onCompleted?.Invoke();
            }
            else
            {
                LogController.Instance.debugError($"Attempt {retryCount + 1} failed: {request.error}");
                retryCount++;

                if (retryCount >= this.maxRetries)
                {
                    LogController.Instance.debugError("Failed to load inventory after maximum retries.");
                    onCompleted?.Invoke();
                    yield break;
                }

                yield return new WaitForSeconds(1f); // Optional delay before retry
            }
        }
    }

    public IEnumerator useHelpTool(int toolId = -1, Action onCompleted = null)
    {
        string api = APIConstant.UpdateUseOfHelpToolAPI(LoaderConfig.Instance);
        LogController.Instance?.debug("called helpTool Inventory api: " + api);

        string jsonBody = JsonUtility.ToJson(new HelpToolRequest
        {
            help_tool_id = toolId,
            amount = 1
        });

        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        int retryCount = 0;
        bool requestSuccessful = false;

        while (retryCount < this.maxRetries && !requestSuccessful)
        {
            UnityWebRequest request = new UnityWebRequest(api, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.certificateHandler = new WebRequestSkipCert();

            request.SetRequestHeader("accept", "application/json");
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + LoaderConfig.Instance.apiManager.jwt);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                requestSuccessful = true;
                LogController.Instance.debug("Success: " + request.downloadHandler.text);
                onCompleted?.Invoke();
            }
            else
            {
                LogController.Instance.debugError($"Attempt {retryCount + 1} failed: {request.error}");
                retryCount++;

                if (retryCount >= this.maxRetries)
                {
                    LogController.Instance.debugError("Failed to use help tool after maximum retries.");
                    onCompleted?.Invoke();
                    yield break;
                }

                yield return new WaitForSeconds(1f); // Optional delay before retry
            }
        }
    }

    public IEnumerator AddCurrency(int amount, Action onCompleted = null)
    {
        string api = APIConstant.GameAddCurrencyAPI(LoaderConfig.Instance);
        LogController.Instance?.debug("Called Add Currency API: " + api);

        // Prepare JSON body
        string jsonBody = $"{{\"amount\": {amount}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        int retryCount = 0;
        bool requestSuccessful = false;

        while (retryCount < this.maxRetries && !requestSuccessful)
        {
            using (UnityWebRequest www = new UnityWebRequest(api, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("accept", "application/json");
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", "Bearer " + LoaderConfig.Instance.apiManager.jwt);
                www.certificateHandler = new WebRequestSkipCert();

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    requestSuccessful = true;
                    LogController.Instance.debug("Success added currency: " + www.downloadHandler.text);
                    onCompleted?.Invoke();
                }
                else
                {
                    LogController.Instance.debugError($"Attempt {retryCount + 1} failed: {www.error}");
                    retryCount++;

                    if (retryCount >= this.maxRetries)
                    {
                        LogController.Instance.debugError("Failed to add currency after maximum retries.");
                        onCompleted?.Invoke();
                        yield break;
                    }

                    yield return new WaitForSeconds(1f); // Optional delay before retry
                }
            }
        }
    }

    public IEnumerator postGameAppSetting(Action onCompleted = null)
    {
        string api = APIConstant.GameAppQuestionDataAPI(LoaderConfig.Instance, LoaderConfig.Instance.RoAppDataKey, this.jwt);
        LogController.Instance?.debug("called load question api from rainbowOne: " + api);
        WWWForm form = new WWWForm();
        int retryCount = 0;
        bool requestSuccessful = false;

        while (retryCount < this.maxRetries && !requestSuccessful)
        {
            using (UnityWebRequest www = UnityWebRequest.Post(api, form))
            {
                // Set headers
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("typ", "jwt");
                www.SetRequestHeader("alg", "HS256");
                www.certificateHandler = new WebRequestSkipCert();
                // Send the request and wait for a response
                yield return www.SendWebRequest();

                // Check for errors
                if (www.result != UnityWebRequest.Result.Success)
                {
                    retryCount++;
                    this.HandleError("Error: " + www.error + "Retrying..." + retryCount, null, true);
                    yield return new WaitForSeconds(2);
                }
                else
                {
                    requestSuccessful = true;
                    string responseText = www.downloadHandler.text;
                    //LogController.Instance?.debug("www.downloadHandler.text: " + responseText);

                    int jsonStartIndex = responseText.IndexOf("{\"questions\":");
                    if (jsonStartIndex != -1)
                    {
                        string jsonData = responseText.Substring(jsonStartIndex);
                        var jsonNode = JSONNode.Parse(jsonData);
                        //LogController.Instance?.debug("jsonNode: " + jsonNode.ToString());

                        this.questionJson = jsonNode[APIConstant.QuestionDataHeaderName].ToString(); // Question json data;
                        //LogController.Instance?.debug("questionJson: " + this.questionJson);
                        this.accountJson = jsonNode["account"].ToString(); // Account json data;
                        //LogController.Instance?.debug("accountJson: " + this.accountJson);
                        string accountUidString = jsonNode["account"]["uid"];
                        int accountUid = int.Parse(accountUidString);
                        this.accountUid = accountUid;

                        if (jsonNode["photo"] != null) this.photoDataUrl = jsonNode["photo"].ToString(); // Account json data;
                        if (jsonNode["setting"] != null) this.gameSettingJson = jsonNode["setting"].ToString();
                        LogController.Instance?.debug("gameSettingJson: " + this.gameSettingJson);
                        if (jsonNode["payloads"] != null) this.payloads = jsonNode["payloads"].ToString();
                        LogController.Instance?.debug("payloads: " + this.payloads);

                        if (!string.IsNullOrEmpty(this.gameSettingJson) && this.gameSettingJson != "{}")
                        {
                            if (jsonNode["setting"]["lang"] != null) LoaderConfig.Instance.Lang = int.Parse(jsonNode["setting"]["lang"].Value);
                            LoaderConfig.Instance.gameSetup.gamePageName = jsonNode["setting"]["title"].Value;
                            this.settings.gameTime = jsonNode["setting"]["game_time"] != null ? jsonNode["setting"]["game_time"] : null;
                            string bgImagUrl = jsonNode["setting"]["background_image_url"] != null ?
                                jsonNode["setting"]["background_image_url"].ToString().Replace("\"", "") : null;
                            string gamePreviewUrl = jsonNode["setting"]["game_preview_image"] != null ?
                                jsonNode["setting"]["game_preview_image"].ToString().Replace("\"", "") : null;
                            this.settings.instructionContent = jsonNode["setting"]["instruction"] != null ?
                                jsonNode["setting"]["instruction"].ToString().Replace("\"", "") : null;

                            LoaderConfig.Instance.gameSetup.gameTime = this.settings.gameTime;

                            if (bgImagUrl != null)
                            {
                                if (!bgImagUrl.StartsWith("https://") || !bgImagUrl.StartsWith(APIConstant.blobServerRelativePath))
                                    this.settings.backgroundImageUrl = APIConstant.blobServerRelativePath + bgImagUrl;
                            }
                            if (gamePreviewUrl != null)
                            {
                                if (!gamePreviewUrl.StartsWith("https://") || !gamePreviewUrl.StartsWith(APIConstant.blobServerRelativePath))
                                    this.settings.previewGameImageUrl = APIConstant.blobServerRelativePath + gamePreviewUrl;
                            }
                            ////////Game Customization params/////////
                            SetParams.setCustomParameters(this.settings, jsonNode);
                        }

                        if (this.debugText != null)
                        {
                            this.debugText.text += "Question Data: " + this.questionJson + "\n\n ";
                            this.debugText.text += "Account: " + this.accountJson + "\n\n ";
                            this.debugText.text += "Photo: " + this.photoDataUrl + "\n\n ";
                            this.debugText.text += "Setting: " + this.gameSettingJson + "\n\n ";
                            this.debugText.text += "PayLoad: " + this.payloads + "\n\n ";
                            this.debugText.text += "Is Logined: " + this.IsLogined;
                        }

                        if (!string.IsNullOrEmpty(this.photoDataUrl) && this.photoDataUrl != "null")
                        {
                            string modifiedPhotoDataUrl = photoDataUrl.Replace("\"", "");

                            string imageUrl = modifiedPhotoDataUrl;
                            if (!modifiedPhotoDataUrl.StartsWith("https://"))
                            {
                                imageUrl = "https:" + modifiedPhotoDataUrl;
                            }
                            //LogController.Instance?.debug($"Downloading People Icon!!{imageUrl}");
                            yield return LoaderConfig.Instance.gameSetup.Load("", imageUrl, _peopleIcon =>
                            {
                                //LogController.Instance?.debug($"Downloaded People Icon!!");
                                this.peopleIcon = _peopleIcon;
                            });
                        }

                        if (jsonNode["account"] != null && !string.IsNullOrEmpty(this.accountJson))
                        {
                            var name = jsonNode["account"]["display_name"].ToString();
                            if (!string.IsNullOrWhiteSpace(name) && name != "null" && name != null)
                            {
                                this.loginName = name.Replace("\"", "");
                                //LogController.Instance?.debug("Display name: " + this.loginName);
                            }
                            else
                            {
                                //LogController.Instance?.debug("Display name is empty. use first name and last name");
                                var first_name = jsonNode["account"]["first_name"].ToString().Replace("\"", "");
                                var last_name = jsonNode["account"]["last_name"].ToString().Replace("\"", "");
                                this.loginName = last_name + " " + first_name;
                            }
                        }

                        //E.g
                        //Debug.Log(jsonNode["account"]["display_name"].ToString());
                        LogController.Instance?.debug(this.questionJson);
                        onCompleted?.Invoke();
                    }
                    else
                    {
                        this.HandleError("wrong json start index.", onCompleted, true);
                    }
                }
            }
        }
        if (!requestSuccessful)
        {
            this.errorMessage = "Failed to get a successful response after " + maxRetries + " attempts.";
            LogController.Instance?.debug(this.errorMessage);
            this.IsShowLoginErrorBox = true;
            onCompleted?.Invoke();
        }
    }

    public IEnumerator SubmitAnswer(Action onCompleted = null)
    {
        if (string.IsNullOrEmpty(this.payloads) || this.accountUid == -1 || string.IsNullOrEmpty(this.jwt))
        {
            LogController.Instance?.debug("Invalid parameters: payloads, accountUid, or jwt is null or empty.");
            yield break;
        }

        string api = APIConstant.SubmitAnswerAPI(LoaderConfig.Instance, this.payloads, this.accountUid, this.jwt);
        LogController.Instance?.debug("called submit marks api: " + api);
        WWWForm form = new WWWForm();
        int retryCount = 0;
        bool requestSuccessful = false;

        while (retryCount < this.maxRetries && !requestSuccessful)
        {
            using (UnityWebRequest www = UnityWebRequest.Post(api, form))
            {
                // Set headers
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("typ", "jwt");
                www.SetRequestHeader("alg", "HS256");
                www.certificateHandler = new WebRequestSkipCert();
                // Send the request and wait for a response
                yield return www.SendWebRequest();

                // Check for errors
                if (www.result != UnityWebRequest.Result.Success)
                {
                    retryCount++;
                    this.HandleError("Error: " + www.error + "Retrying..." + retryCount, null, true);
                    yield return new WaitForSeconds(2);
                }
                else
                {
                    requestSuccessful = true;
                    string responseText = www.downloadHandler.text;
                    LogController.Instance?.debug("Success to submit responseText: " + responseText);
                    // Format the JSON response for better readability
                    try
                    {
                        var parsedJson = JSONNode.Parse(responseText);
                        string prettyJson = parsedJson.ToString();
                        LogController.Instance?.debug("Success to submit answers: " + prettyJson);
                        onCompleted?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        this.HandleError("Failed to parse JSON: " + ex.Message, null, true);
                    }
                }
            }
        }

        if (!requestSuccessful)
        {
            this.HandleError("Failed to call upload marks response after " + maxRetries + " attempts.", onCompleted, true);
        }
    }


    public IEnumerator ExitGameRecord(Action onCompleted = null)
    {
        if (string.IsNullOrEmpty(this.payloads) || this.accountUid == -1 || string.IsNullOrEmpty(this.jwt))
        {
            LogController.Instance?.debug("Invalid parameters: payloads, accountUid, or jwt is null or empty.");
            yield break;
        }

        string jsonData = $"[{{ \"payloads\": {this.payloads} }}]";
        WWWForm formData = new WWWForm();
        formData.AddField("api", "ROGame.quit_game");
        formData.AddField("jwt", this.jwt); // Add the JWT to the form
        formData.AddField("json", jsonData);

        string endGameApi = APIConstant.EndGameAPI(LoaderConfig.Instance);
        int retryCount = 0;
        bool requestSuccessful = false;

        while (retryCount < this.maxRetries && !requestSuccessful)
        {
            using (UnityWebRequest www = UnityWebRequest.Post(endGameApi, formData))
            {
                // Send the request and wait for a response
                yield return www.SendWebRequest();

                // Handle the response
                if (www.result != UnityWebRequest.Result.Success)
                {
                    retryCount++;
                    this.HandleError("Error: " + www.error + "Retrying..." + retryCount, null, true);
                    yield return new WaitForSeconds(2);
                }
                else
                {
                    requestSuccessful = true;
                    string responseText = www.downloadHandler.text;

                    // Format the JSON response for better readability
                    try
                    {
                        var parsedJson = JSONNode.Parse(responseText);
                        string prettyJson = parsedJson.ToString();
                        LogController.Instance?.debug("Success to post end game api: " + prettyJson);
                        onCompleted?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        this.HandleError("Failed to parse JSON: " + ex.Message, null, true);
                    }
                }
            }
        }

        if (!requestSuccessful)
        {
            this.HandleError("Failed to call endgame api after " + maxRetries + " attempts.", onCompleted, true);
        }
    }

    public IEnumerator postScoreToStarAPI(string scores, Action<int[]> onCompleted = null)
    {
        string api = APIConstant.GamePostScoreToStarAPI(LoaderConfig.Instance, this.payloads, this.jwt, scores);
        LogController.Instance?.debug("called post score to star api: " + api);

        WWWForm form = new WWWForm(); // No fields needed, parameters are in the URL
        int retryCount = 0;
        bool requestSuccessful = false;

        while (retryCount < this.maxRetries && !requestSuccessful)
        {
            using (UnityWebRequest www = UnityWebRequest.Post(api, form))
            {
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("typ", "jwt");
                www.SetRequestHeader("alg", "HS256");
                www.certificateHandler = new WebRequestSkipCert();

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    retryCount++;
                    this.HandleError("Error: " + www.error + " Retrying..." + retryCount, null, true);
                    yield return new WaitForSeconds(2);
                }
                else
                {
                    requestSuccessful = true;
                    string responseText = www.downloadHandler.text;
                    try
                    {
                        var parsedJson = SimpleJSON.JSONNode.Parse(responseText);
                        var starsNode = parsedJson["stars"];
                        int[] stars = new int[starsNode.Count];
                        for (int i = 0; i < starsNode.Count; i++)
                        {
                            stars[i] = starsNode[i].AsInt;
                        }
                        LogController.Instance?.debug("Stars: " + string.Join(",", stars));
                        onCompleted?.Invoke(stars);
                    }
                    catch (Exception ex)
                    {
                        this.HandleError("Failed to parse JSON: " + ex.Message, null, true);
                        onCompleted?.Invoke(new int[0]);
                    }
                }
            }
        }

        if (!requestSuccessful)
        {
            this.HandleError("Failed to call post score to star API after " + maxRetries + " attempts.", null, true);
            onCompleted?.Invoke(new int[0]);
        }
    }

    public IEnumerator restartGameAPI(Action onCompleted = null)
    {
        string api = APIConstant.RestartGameDataAPI(LoaderConfig.Instance, this.payloads, this.jwt);
        LogController.Instance?.debug("called post restart game api: " + api);

        WWWForm form = new WWWForm(); // No fields needed, parameters are in the URL
        int retryCount = 0;
        bool requestSuccessful = false;

        while (retryCount < this.maxRetries && !requestSuccessful)
        {
            using (UnityWebRequest www = UnityWebRequest.Post(api, form))
            {
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("typ", "jwt");
                www.SetRequestHeader("alg", "HS256");
                www.certificateHandler = new WebRequestSkipCert();

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    retryCount++;
                    this.HandleError("Error: " + www.error + " Retrying..." + retryCount, null, true);
                    yield return new WaitForSeconds(2);
                }
                else
                {
                    requestSuccessful = true;
                    string responseText = www.downloadHandler.text;
                    try
                    {
                        var parsedJson = JSONNode.Parse(responseText);
                        var newPayLoad = parsedJson["payloads"];
                        this.payloads = newPayLoad.ToString();
                        LogController.Instance?.debug("Updated new payLoad, " + this.payloads);
                        onCompleted?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        this.HandleError("Failed to parse payLoad JSON: " + ex.Message, null, true);
                        onCompleted?.Invoke();
                    }
                }
            }
        }

        if (!requestSuccessful)
        {
            this.HandleError("Failed to call restart game api after " + maxRetries + " attempts.", null, true);
            onCompleted?.Invoke();
        }
    }
}

public static class APIConstant
{
    public static string QuestionDataHeaderName = "questions";
    public static string GameDataAPI(LoaderConfig loader, string _bookId = "", string _jwt = "")
    {
        string jsonParameter = string.IsNullOrEmpty(_bookId) ? "[1]" : $"[\"{_bookId}\"]";
        return $"{loader.CurrentHostName}/RainbowOne/index.php/PHPGateway/proxy/2.8/?api=ROGame.get_game_setting&json={jsonParameter}&jwt=" + _jwt;
    }

    public static string RestartGameDataAPI(LoaderConfig loader, string payload = "", string _jwt = "")
    {
        string jsonParameter = $"[{{\"payloads\":{payload}}}]";
        return $"{loader.CurrentHostName}/RainbowOne/index.php/PHPGateway/proxy/2.8/?api=ROGame.start_game_session&json={jsonParameter}&jwt=" + _jwt;
    }

    public static string GamePostScoreToStarAPI(LoaderConfig loader, string payload = "", string _jwt = "", string scores = "")
    {
        string jsonParameter = $"[{{\"payloads\":{payload},\"scores\":{scores},\"gameTotalStars\":{LoaderConfig.Instance.gameSetup.gameTotalStars}}}]";
        return $"{loader.CurrentHostName}/RainbowOne/index.php/PHPGateway/proxy/2.8/?api=ROGame.calStarForPlayers&json={jsonParameter}&jwt={_jwt}";
    }

    public static string GameAddCurrencyAPI(LoaderConfig loader)
    {
        if (string.IsNullOrEmpty(StarwishpartySiteDomain(loader)))
        {
            LogController.Instance?.debug("Current site not support starwish add currency api.");
            return string.Empty;
        }
        return $"{StarwishpartySiteDomain(loader)}/OKAGames/public/index.php/api/accounts/add-currency";
    }

    public static string GameAppQuestionDataAPI(LoaderConfig loader, string _dataKey = "", string _jwt = "")
    {
        string jsonParameter = string.IsNullOrEmpty(_dataKey) ? "[1]" : $"[\"comp-{_dataKey}\"]";
        return $"{loader.CurrentHostName}/RainbowOne/index.php/PHPGateway/proxy/2.8/?api=ROGame.get_game_setting&json={jsonParameter}&jwt=" + _jwt;
    }

    public static string GetStarwishPartyAccountAPI(LoaderConfig loader)
    {
        if (string.IsNullOrEmpty(StarwishpartySiteDomain(loader)))
        {
            LogController.Instance?.debug("Current site not support starwish add currency api.");
            return string.Empty;
        }
        return $"{StarwishpartySiteDomain(loader)}/OKAGames/public/index.php/api/accounts/{loader.apiManager.accountUid}";
    }

    public static string GetHelpToolInventoryAPI(LoaderConfig loader)
    {
        if (string.IsNullOrEmpty(StarwishpartySiteDomain(loader)))
        {
            LogController.Instance?.debug("Current site not support starwish add currency api.");
            return string.Empty;
        }
        return $"{StarwishpartySiteDomain(loader)}/OKAGames/public/index.php/api/help-tools/user/inventory";
    }

    public static string UpdateUseOfHelpToolAPI(LoaderConfig loader)
    {
        if (string.IsNullOrEmpty(StarwishpartySiteDomain(loader)))
        {
            LogController.Instance?.debug("Current site not support starwish add currency api.");
            return string.Empty;
        }
        return $"{StarwishpartySiteDomain(loader)}/OKAGames/public/index.php/api/help-tools/use";
    }

    static string StarwishpartySiteDomain(LoaderConfig loader)
    {
        string domain = "";
        if (isLoginedStarwishPartySite(loader))
        {
            if (loader.CurrentHostName.Contains("www.starwishparty.com") ||
                loader.CurrentHostName.Contains("pro.starwishparty.com") ||
                loader.CurrentHostName.Contains("rainbowone.app") ||
                loader.CurrentHostName.Contains("app.starwishparty.com"))
                domain = "pro.starwishparty.com";
            else
                domain = loader.CurrentHostName;
        }
        else
        {
            return string.Empty;
        }
        return domain;
    }

    public static bool isLoginedStarwishPartySite(LoaderConfig loader)
    {
        bool isLogined = loader.apiManager.IsLogined;
        if (!isLogined) return false;
        bool isLoginedStarwishParty =
                          loader.CurrentHostName.Contains("dev.starwishparty.com") ||
                          loader.CurrentHostName.Contains("uat.starwishparty.com") ||
                          loader.CurrentHostName.Contains("pre.starwishparty.com") ||
                          loader.CurrentHostName.Contains("www.starwishparty.com") ||
                          loader.CurrentHostName.Contains("app.starwishparty.com");
        return isLoginedStarwishParty;
    }

    public static string RainbowOneAPIEndPoint(LoaderConfig loader)
    {
        return $"{loader.CurrentHostName}/RainbowOne/index.php/PHPGateway/proxy/2.8/";
    }

    public static string blobServerRelativePath
    {
        get
        {
            return LoaderConfig.Instance.currentHostName switch
            {
                HostName.dev => "https://okadev.blob.core.windows.net/media/",
                HostName.uat => "https://okauat.blob.core.windows.net/media/",
                HostName.preprod => "https://okapreprod.blob.core.windows.net/media/",
                HostName.prod => "https://oka.blob.core.windows.net/media/",
                _ => throw new NotImplementedException()
            };
        }
    }

    public static string SubmitAnswerAPI(LoaderConfig loader, string playloads, int uid, string _jwt)
    {
        if (loader == null) return null;
        var hostName = loader.CurrentHostName;
        var answer = loader.apiManager.answer;


        int stateDuration = answer.state.duration;
        float stateScore = answer.state.score;
        float statePercent = answer.state.percent;
        float stateProgress = answer.state.progress;

        int correct = answer.currentQA.correctId;
        float currentQADuration = answer.currentQA.duration;
        string currentqid = answer.currentQA.qid;
        int answerId = answer.currentQA.answerId;
        string answerText = answer.currentQA.answerText;
        string correctAnswerText = answer.currentQA.correctAnswerText;
        float currentQAscore = answer.currentQA.score;
        float currentQAPercent = answer.currentQA.percent;

        string jsonPayload = $"[{{\"payloads\":{playloads}," +
        $"\"role\":{{\"uid\":{uid}}}," +
        $"\"state\":{{\"duration\":{stateDuration},\"score\":{stateScore},\"percent\":{statePercent},\"progress\":{stateProgress}}}," +
        $"\"currentQuestion\":{{\"correct\":{correct},\"duration\":{currentQADuration},\"qid\":\"{currentqid}\",\"answer\":{answerId},\"answerText\":\"{answerText}\",\"correctAnswerText\":\"{correctAnswerText}\",\"score\":{currentQAscore},\"percent\":{currentQAPercent}}}}}]";

        string submitAPI = $"{hostName}/RainbowOne/index.php/PHPGateway/proxy/2.8/?api=ROGame.submit_answer&json={jsonPayload}&jwt=" + _jwt;
        return submitAPI;
    }

    public static string EndGameAPI(LoaderConfig loader)
    {
        string endAPI = $"{loader.CurrentHostName}/RainbowOne/index.php/PHPGateway/proxy/2.8/?api=ROGame.quit_game";
        return endAPI;
    }
}

[Serializable]
public class Settings
{
    public string chapterUnit = string.Empty;
    public string previewGameImageUrl;
    public string backgroundImageUrl;
    public string instructionContent = string.Empty;
    public int gameTime = 0;
}
