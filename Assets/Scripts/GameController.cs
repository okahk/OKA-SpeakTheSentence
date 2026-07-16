using UnityEngine;
public class GameController : GameBaseController
{
    public static GameController Instance = null;
    public CharacterSet[] characterSets;
    public Transform parent;
    public Sprite[] defaultAnswerBox;
    public PlayerController playerController;

    protected override void Awake()
    {
        if (Instance == null) Instance = this;
        base.Awake();
    }

    protected override void Start()
    {
        base.Start();

        string result = "";
        string instruction = LoaderConfig.Instance.apiManager.settings.instructionContent;
        if (string.IsNullOrEmpty(instruction))
        {
            result = QuestionManager.Instance.questionData.questions[0].questionHint;
        }
        else
        {
            if (instruction.Contains("-"))
            {
                // Split the string and take the part before the first "-"
                result = instruction.Split('-')[0];
                LogController.Instance.debug(result); // Output: Listen to the audio and speak the words out
                TextToSpeech.Instance?.UpdateTextToAudioFromAPI(result);
            }
            else
            {
                result = instruction;
                LogController.Instance.debug("No '-' found in the string.");
            }
        }
        //Add real time download question hint from API;
        TextToSpeech.Instance?.UpdateTextToAudioFromAPI(result);
    }

    void createPlayer()
    {
        this.playerController.gameObject.name = "Player_" + 0;
        this.playerController.UserId = 0;
        this.playerController.Init(this.characterSets[0], this.defaultAnswerBox);

        if (LoaderConfig.Instance != null && LoaderConfig.Instance.apiManager.IsLogined)
        {
            var _playerName = LoaderConfig.Instance?.apiManager.loginName;
            var icon = LoaderConfig.Instance.apiManager.peopleIcon != null ?
                SetUI.ConvertTextureToSprite(LoaderConfig.Instance.apiManager.peopleIcon as Texture2D) :
                SetUI.ConvertTextureToSprite(this.characterSets[0].defaultIcon as Texture2D);

            this.playerController.UserName = _playerName;
            this.playerController.updatePlayerIcon(true, _playerName, icon);
        }
        else
        {
            var icon = SetUI.ConvertTextureToSprite(this.characterSets[0].defaultIcon as Texture2D);
            this.playerController.updatePlayerIcon(true, "", icon);
        }
    }


    public override void enterGame()
    {
        base.enterGame();
        this.createPlayer();
    }

    public override void endGame()
    {
        bool showSuccess = false;
        var loader = LoaderConfig.Instance;
        bool isLogined = loader != null && loader.apiManager.IsLogined;

        if (isLogined)
        {
            var resultPageCg = this.endGamePage.messageBg.GetComponent<CanvasGroup>();
            bool isLoginedStarwishParty = APIConstant.isLoginedStarwishPartySite(loader);

            string playerScore = this.playerController.Score.ToString();
            bool hasExitedGameRecord = false;

            SetUI.SetInteract(resultPageCg, false);
            string scoresJson = "[" + string.Join(",", playerScore) + "]";
            StartCoroutine(
                loader.apiManager.postScoreToStarAPI(scoresJson, (stars) => {

                    if (this.playerController != null)
                    {
                        if (isLoginedStarwishParty)
                        {
                            if (stars[0] > 0)
                            {
                                StartCoroutine(loader.apiManager.AddCurrency(stars[0], () =>
                                {
                                    LogController.Instance.debug("Score to Star API call completed!");
                                }));
                            }
                        }

                        int star = (stars != null && stars.Length == 1) ? stars[0] : -1;
                        this.endGamePage.updateFinalScoreWithStar(0, playerController.Score, star, () =>
                        {
                            if (this.endGamePage.scoreEndings[0].starNumber > 0)
                            {
                                showSuccess = true;
                            }
                            if (!hasExitedGameRecord)
                            {
                                hasExitedGameRecord = true;
                                StartCoroutine(LoaderConfig.Instance.apiManager.ExitGameRecord(() =>
                                {
                                    SetUI.SetInteract(resultPageCg, true);
                                }));
                            }
                        });
                    }

                    this.endGamePage.setStatus(true, showSuccess);
                    base.endGame();
                })
            );
        }
        else
        {
            if (playerController != null)
            {
                this.endGamePage.updateFinalScore(0, this.playerController.Score, () =>
                {
                    if (this.endGamePage.scoreEndings[0].starNumber > 0)
                    {
                        showSuccess = true;
                    }
                });
            }
            this.endGamePage.setStatus(true, showSuccess);
            base.endGame();
        }
    }

    public void UpdateNextQuestion()
    {
        LogController.Instance?.debug("Next Question");
        var questionController = QuestionController.Instance;

        if (questionController != null) {
            questionController.nextQuestion();
        }       
    }

   
    private void Update()
    {
        if(!this.playing) return;

        if (Input.GetKeyDown(KeyCode.F1))
        {
            this.UpdateNextQuestion();
        }
    }

    /*
    [DllImport("__Internal")]
    public static extern void StartRecord();
    [DllImport("__Internal")]
    public static extern void StopRecord();

    private int m_valuePartCount = 0;
    private int m_getDataLength = 0;
    private int m_audioLength = 0;
    private string[] m_audioData = null;
    private List<byte[]> m_audioClipDataList;
    private string m_currentRecorderSign;
    public void GetAudioData(string _audioDataString)
    {
        if (_audioDataString.Contains("Head"))
        {
            string[] _headValue = _audioDataString.Split('|');
            m_valuePartCount = int.Parse(_headValue[1]);
            m_audioLength = int.Parse(_headValue[2]);
            m_currentRecorderSign = _headValue[3];
            m_audioData = new string[m_valuePartCount];
            m_getDataLength = 0;
            LogController.Instance.debug("接收数据头：" + m_valuePartCount + "   " + m_audioLength);
        }
        else if (_audioDataString.Contains("Part"))
        {
            string[] _headValue = _audioDataString.Split('|');
            int _dataIndex = int.Parse(_headValue[1]);
            m_audioData[_dataIndex] = _headValue[2];
            m_getDataLength++;
            if (m_getDataLength == m_valuePartCount)
            {
                StringBuilder stringBuilder = new StringBuilder();
                for (int i = 0; i < m_audioData.Length; i++)
                {
                    stringBuilder.Append(m_audioData[i]);
                }
                string _audioDataValue = stringBuilder.ToString();
                LogController.Instance.debug("接收长度:" + _audioDataValue.Length + " 需接收长度:" + m_audioLength);
                int _index = _audioDataValue.LastIndexOf(',');
                string _value = _audioDataValue.Substring(_index + 1, _audioDataValue.Length - _index - 1);
                byte[] data = Convert.FromBase64String(_value);
                LogController.Instance.debug("已接收长度 :" + data.Length);

                if (m_currentRecorderSign == "end")
                {
                    int _audioLength = data.Length;
                    for (int i = 0; i < m_audioClipDataList.Count; i++)
                    {
                        _audioLength += m_audioClipDataList[i].Length;
                    }
                    byte[] _audioData = new byte[_audioLength];
                    LogController.Instance.debug("总长度 :" + _audioLength);
                    int _audioIndex = 0;
                    data.CopyTo(_audioData, _audioIndex);
                    _audioIndex += data.Length;
                    LogController.Instance.debug("已赋值0:" + _audioIndex);
                    for (int i = 0; i < m_audioClipDataList.Count; i++)
                    {
                        m_audioClipDataList[i].CopyTo(_audioData, _audioIndex);
                        _audioIndex += m_audioClipDataList[i].Length;
                        LogController.Instance.debug("已赋值 :" + _audioIndex);
                    }

                    WAV wav = new WAV(_audioData);
                    AudioClip _audioClip = AudioClip.Create("TestWAV", wav.SampleCount, 1, wav.Frequency, false);
                    _audioClip.SetData(wav.LeftChannel, 0);


                    RecordAudio.Instance.originalTrimmedClip = _audioClip;
                    RecordAudio.Instance.SendAudioClipFromJavascriptToAPI(_audioData);
                    LogController.Instance.debug("音频设置成功,已设置到unity。" + _audioClip.length + "  " + _audioClip.name);

                    m_audioClipDataList.Clear();
                }

                m_audioData = null;
            }
        }
    }*/
}
