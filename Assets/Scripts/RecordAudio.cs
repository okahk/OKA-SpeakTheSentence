using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class RecordAudio : RecorderManager
{
    public static RecordAudio Instance = null;
    void Awake()
    {
        if (Instance == null) Instance = this;
    }
    public enum Stage
    {
        Record=0,
        Recording = 1,
        UploadClip = 2,
        PlaybackResult = 3,
    }
    public Stage stage = Stage.Record;
    //public RecognitionAPI recognitionAPI = RecognitionAPI.recognize_tts;
    public TextMeshProUGUI debugText, answerText, submitAudioText;
    public Color32 answerTextOriginalColor;
    public RawImage answerBox;
    public Texture[] answerBoxTexs;

    public bool useHighPassFilter = false;
    [Header("Audio Pages for different process")]
    public CanvasGroup[] pages;

    [Header("Result Page for different control button")]
    public CanvasGroup[] resultBtns;

    [SerializeField] private AudioSource playbackSource;
    [SerializeField] private CanvasGroup recordButton;
    [SerializeField] private CanvasGroup stopButton;
    [SerializeField] private RawImage playbackButton;
    [SerializeField] private Texture[] playbackBtnTexs;
    [SerializeField] private Text stopRecordText, playbackText, accurancyTitle, accurancyText;
    [SerializeField] private int maxRecordLength = 10;
    public WaveformVisualizer waveformVisualizer;
    [SerializeField] private Slider playbackSlider;
    [SerializeField] private CanvasGroup remindRecordTip;
    [SerializeField] private Button qa_audio_btn;
    [SerializeField] public CanvasGroup hintBox, remindRecordBox, processButtonCg, playBackStatusText;
    public UnityEngine.Audio.AudioMixerGroup recordingMixerGroup;

    private bool isRecording = false;
    public bool isPlaying = false;
    private float recordingTime = 0f;
    public SttResponse sttResponse;
    public RecognitionResult recognitionResult;
    public GPTTranscribeResult gptTranscribeResult;
    private bool grantedMicrophone = false;
    public int passAccuracyScore = 60;
    public int passPronScore = 60;
    public bool isInitialized = false;
    private string ApiUrl = "";
    private string JwtToken = "eyJ0eXAiOiJqd3QiLCJhbGciOiJIUzI1NiJ9.eyJsb2dfZW5hYmxlZCI6IjEiLCJ0b2tlbiI6IjUyNzcwMS04MTcyNGIyYTIxODk4YTE2NTA0ZTZiMTg0ZWZlMWQ5Mjc2OGIyYWM1YmI2ZmExMDc4NDVlZjM1MDRjNTY3NDBlIiwiZXhwaXJlcyI6MTgwODUzNjQ5NSwicmVuZXdfZW5hYmxlZCI6MSwidGltZSI6IjIwMjUtMDQtMjQgMDM6MTQ6NTUgR01UIiwidWlkIjoiNTI3NzAxIiwidXNlcl9yb2xlIjoiMiIsInNjaG9vbF9pZCI6IjMxNiIsImlwIjoiOjoxIiwidmVyc2lvbiI6bnVsbCwiZGV2aWNlIjoidW5rbm93biJ9.SO79u9MBCflyYh_TcsIBG740pWXgKPZOAsGNZESkoqo";

    public WordDetail[] wordDetails;
    private Coroutine loadingTextCoroutine;
    //private string hostName = "dev.openknowledge.hk";
    //private string hostName = "www.rainbowone.app";
    void Start()
    {
        StartCoroutine(this.initMicrophonePermission(1f));
        if (this.playbackSlider != null)
        {
            this.playbackSlider.onValueChanged.AddListener(OnSliderValueChanged);
            this.playbackSlider.minValue = 0;
            this.playbackSlider.maxValue = 1;
        }
        this.passAccuracyScore = LoaderConfig.Instance.gameSetup.passAccuracyScore;
        this.passPronScore = LoaderConfig.Instance.gameSetup.passPronScore;

        SetUI.Set(this.recordButton, false, 0f, 0.5f);
    }

    public void Init()
    {
        this.switchPage(Stage.Record);
    }

    void Update()
    {
        if (this.qa_audio_btn != null) this.qa_audio_btn.interactable = !this.isRecording;
        if (this.isRecording && this.clip)
        {
            recordingTime += Time.deltaTime;

            if (recordingTime >= maxRecordLength)
            {
                StopRecording();
                return;
            }

            // Get the current microphone position
            int micPosition = Microphone.GetPosition(null);

            if (micPosition > 0 && micPosition <= this.clip.samples)
            {
                int sampleCount = Mathf.Min(micPosition, this.clip.samples);
                float[] samples = new float[sampleCount];
                this.clip.GetData(samples, 0);
                float gain = this.GetPlatformSpecificGain();
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
                }
                this.waveformVisualizer?.UpdateWaveform(samples);
            }

            if (this.stopRecordText != null)
            {
                int totalSeconds = Mathf.Min((int)recordingTime, maxRecordLength);
                TimeSpan timeSpan = TimeSpan.FromSeconds(totalSeconds);
                this.stopRecordText.text = timeSpan.ToString(@"hh\:mm\:ss");
            }
            this.UpdateUI($"Recording...");
        }

        if (isPlaying && playbackSource != null)
        {
            // Update the slider value during playback
            if (this.playbackSlider != null && playbackSource.clip != null)
            {
                this.playbackSlider.value = playbackSource.time / playbackSource.clip.length;
                this.updatePlayBackText(playbackSource.time);
            }

            // Stop playback if the audio has reached the end
            if (!playbackSource.isPlaying && Mathf.Approximately(playbackSource.time, playbackSource.clip.length - 0.01f))
            {
                LogController.Instance.debug("Finished playback");
                this.StopPlayback();
            }
        }
    }

    private void ApplyHighPassFilter(float[] samples, int channels, float cutoffFrequency, float sampleRate)
    {
        float rc = 1.0f / (cutoffFrequency * 2 * Mathf.PI);
        float dt = 1.0f / sampleRate;
        float alpha = rc / (rc + dt);

        // Process each channel independently
        for (int channel = 0; channel < channels; channel++)
        {
            float previousSample = samples[channel];
            for (int i = channel; i < samples.Length; i += channels)
            {
                float currentSample = samples[i];
                samples[i] = alpha * (samples[i] - previousSample);
                previousSample = currentSample;
            }
        }
    }

    public void PlayAgainHint()
    {
        QuestionController.Instance?.currentQuestion.stopAudio();
        float defaultDelay = !this.isInitialized ? 1f : 0f;
        this.hintBox.GetComponent<TextToSpeech>()?.PlayAudio(() =>
        {
            SetUI.Set(this.hintBox, true);
            SetUI.Set(this.remindRecordBox, false);
        },
        () =>
        {
            SetUI.Set(this.hintBox, false);
            SetUI.Set(this.remindRecordBox, true);
            if (!this.isInitialized)
            {
                GameController.Instance?.UpdateNextQuestion();

                string checkSingleWord = QuestionController.Instance.currentQuestion.qa.checkSingleWord;
                string hasPrompt = QuestionController.Instance.currentQuestion.qa.prompt;
                LogController.Instance.debug("Current is checkSingleWord: " + checkSingleWord);
                if (!string.IsNullOrEmpty(checkSingleWord) && checkSingleWord == "1")
                    this.detectMethod = DetectMethod.Word;

                if (!string.IsNullOrEmpty(hasPrompt))
                    this.detectMethod = DetectMethod.prompt;

                SetUI.Set(this.recordButton, true, 0f);
                this.isInitialized = true;
            }
        },
        defaultDelay
        );
    }

    void switchPage(Stage _stage)
    {
        this.stage = _stage;
        LogController.Instance.debug($"Current Recording Stage: {this.stage}"); 
        switch (this.stage)
        {
            case Stage.Record:
                this.ResetRecorder();
                if (!this.isInitialized) this.PlayAgainHint();
                break;
            case Stage.Recording:
                this.StopPlayback();
                QuestionController.Instance?.currentQuestion.stopAudio();
                QuestionController.Instance?.currentQuestion.setInteractiveOfQuestionBoards(false);
                SetUI.SetGroup(this.pages, 1);
                StartCoroutine(this.delayEnableStopRecorder());
                break;
            case Stage.UploadClip:
                AudioController.Instance?.fadingBGM(true, 1f);
                QuestionController.Instance?.currentQuestion.setInteractiveOfQuestionBoards(true);
                break;
            case Stage.PlaybackResult:
                SetUI.SetGroup(this.pages, 2);
                break;
        }
    }

    IEnumerator delayEnableStopRecorder()
    {
        yield return new WaitForSeconds(1f);
        SetUI.Set(this.stopButton, true, 0f, 1f);
    }

    public void controlResultPage(int showBtnId=-1)
    {
        SetUI.SetGroup(this.resultBtns, showBtnId);
        if(showBtnId == 2)
        {
           this.ShowDirectCorrectAnswer();
        }
    }

    private IEnumerator initMicrophonePermission(float _delay = 1f)
    {
        if (this.grantedMicrophone) yield break;
        Microphone.Start("", true, 1, 16000);
        yield return new WaitForSeconds(_delay);
        Microphone.End(null);
        LogController.Instance?.debug("Microphone access granted.");
        this.grantedMicrophone = true;
        if(this.isInitialized)
        {
            SetUI.Set(this.recordButton, true, 0f, 0.5f);
        }
    }

    public void StartRecording()
    {
        if (this.hintBox != null)
        {
            var ttS = this.hintBox.GetComponent<TextToSpeech>();
            if (ttS != null)
            {
                ttS.StopAudio();
            }
            SetUI.Set(this.hintBox, false);
        }
        if (this.remindRecordBox != null)
        {
            SetUI.Set(this.remindRecordBox, true);
        }

        if (this.isRecording) return;
        if (!this.grantedMicrophone)
        {
            LogController.Instance.debug("Microphone permission not granted. Please allow access and try again.");
            // Optionally, re-trigger permission request
            StartCoroutine(this.initMicrophonePermission(1f));
            return;
        }
        AudioController.Instance?.fadingBGM(false, 0f);
        LogController.Instance?.debug($"Recording started: {this.isRecording}");
        var microphoneDevices = LoaderConfig.Instance.microphoneDevice;
        if (!microphoneDevices.HasMicrophoneDevices)
        {
            LogController.Instance?.debug("No microphone devices available. use default microphone");
            this.clip = Microphone.Start("", true, maxRecordLength, 44100);
        }
        else
        {

            this.clip = Microphone.Start(microphoneDevices.selectedDeviceName, true, maxRecordLength, 44100);

            if (this.loadingTextCoroutine != null)
                StopCoroutine(this.loadingTextCoroutine);

            this.loadingTextCoroutine = StartCoroutine(AnimateLoadingText("Recording"));
        }

        if (this.clip)
        {
            this.waveformVisualizer?.ClearTexture();
            this.isRecording = true;
            recordingTime = 0f;
            this.switchPage(Stage.Recording);
        }
        else
        {
            this.UpdateUI("Failed to start recording.");
        }

    }

    private float GetPlatformSpecificGain()
    {
#if UNITY_EDITOR
        // Use default gain for the editor
        return 4.0f;
#elif UNITY_WEBGL
    return 2f;
#elif UNITY_IOS
    // iPad browsers may already apply AGC, so use lower gain
    return 1.0f;
#else
    // Default gain for other platforms
    return 2f;
#endif
    }

    private float[] NormalizeSamples(float[] samples, int channels, float gain, int sampleRate)
    {
        if (samples == null || samples.Length == 0) return samples;

        // Work on a copy to avoid mutating the original playback samples
        float[] proc = (float[])samples.Clone();

#if UNITY_WEBGL && !UNITY_EDITOR
    // Skip heavy processing on WebGL (keep original)
    return proc;
#endif

        // Optional high-pass filter
        if (this.useHighPassFilter)
        {
            ApplyHighPassFilter(proc, channels, cutoffFrequency: 50f, sampleRate: sampleRate);
        }

        // Find max amplitude
        float maxAmp = 0f;
        for (int i = 0; i < proc.Length; i++)
        {
            float a = Mathf.Abs(proc[i]);
            if (a > maxAmp) maxAmp = a;
        }

        if (maxAmp > 0f)
        {
            float scale = gain / maxAmp;
            for (int i = 0; i < proc.Length; i++)
            {
                proc[i] = Mathf.Clamp(proc[i] * scale, -1f, 1f);
            }
        }

        return proc;
    }


    private IEnumerator AnimateLoadingText(string content = "")
    {
        string[] loadingStates = { $"{content}...", $"{content}..", $"{content}." };
        int index = 0;
        while (true)
        {
            if (this.submitAudioText != null)
                this.submitAudioText.text = loadingStates[index];
            index = (index + 1) % loadingStates.Length;
            yield return new WaitForSeconds(0.5f); // Adjust speed as needed
        }
    }

    public void StopRecording()
    {
        if (!isRecording)
        {
            LogController.Instance?.debug("StopRecording called but not currently recording.");
            return;
        }

        if (this.loadingTextCoroutine != null)
            StopCoroutine(this.loadingTextCoroutine);

        this.loadingTextCoroutine = StartCoroutine(AnimateLoadingText("Processing"));
        SetUI.Set(this.processButtonCg, true);
        int micPosition = Microphone.GetPosition(LoaderConfig.Instance.microphoneDevice.selectedDeviceName);
        bool wasRecording = Microphone.IsRecording(LoaderConfig.Instance.microphoneDevice.selectedDeviceName);

        if (!wasRecording || micPosition <= 0)
        {
            LogController.Instance?.debug("Microphone is not ready or no content recorded, resetting state.");
            this.UpdateUI("Microphone is not ready and no contents recorded, please retry");
            isRecording = false;
            // Optionally reset UI to a safe state
            this.ResetRecorder();
            return;
        }
        Microphone.End(LoaderConfig.Instance.microphoneDevice.selectedDeviceName);

        isRecording = false;
        this.switchPage(Stage.UploadClip);
        StartCoroutine(this.TrimAudioClip(micPosition));
    }

    /*
    public void SendAudioClipFromJavascriptToAPI(byte[] wavData)
    {
        StartCoroutine(this.SendToAPIForRecognitionDirectly(wavData));
    }

    private IEnumerator SendToAPIForRecognitionDirectly(byte[] wavData)
    {
        if (this.originalTrimmedClip == null)
        {
            LogController.Instance?.debugError("originalTrimmedClip is null");
            yield return null;
        }
        LogController.Instance?.debug($"this.originalTrimmedClip: {this.originalTrimmedClip.name}");

        // Parallel API calls
        bool azureDone = false;
        this.ttsDone = false;
        string azureTranscript = null;
        string azureError = null, ttsError = null;
        this.ttsFailure = false;

        // Start Azure STT
        StartCoroutine(SendAudioToAzureApi(wavData, null,
            (transcript) => {
                azureTranscript = transcript;
                azureDone = true;
            },
            (error) => {
                azureError = error;
                azureDone = true;
            }
        ));

        // Start TTS recognition
        StartCoroutine(UploadAudioFileServer(wavData, null,
            (response) => {
                LogController.Instance.debug($"Start to pass to recognition request");
            },
            (error) => {
                ttsError = error;
                this.ttsDone = true;
            }
        ));

        // Wait for both to finish
        while (!azureDone || !this.ttsDone)
            yield return null;

        if (ttsError != null && azureError != null)
        {
            this.UpdateUI($"Both recognitions failed.\nAzure error: {azureError}\nTTS error: {ttsError}");
            this.switchPage(Stage.Record);
        }
        else
        {
            // Final UI/page update
            if (!this.ttsFailure)
            {
                this.UpdateUI("TTS recognition passed.");
            }
            else
            {
                this.UpdateUI("TTS failed, fallback to Azure STT result.");
                this.switchPage(Stage.PlaybackResult);
                var playerController = this.GetComponent<PlayerController>();
                if (playerController != null)
                {
                    playerController.submitAnswer(this.answerText.text);
                }
            }
        }
    }*/

    private IEnumerator TrimAudioClip(int micPosition)
    {
        float actualLength = micPosition / (float)this.clip.frequency;
        yield return new WaitForEndOfFrame();
        AudioClip trimmedClip = AudioClip.Create(this.clip.name, micPosition, this.clip.channels, this.clip.frequency, false);
        float[] samples = new float[micPosition * this.clip.channels];
        this.clip.GetData(samples, 0);
        trimmedClip.SetData(samples, 0);

        this.playBackClip = trimmedClip;
        float[] processedSamples = NormalizeSamples(samples, trimmedClip.channels, 4f, trimmedClip.frequency);
        AudioClip processedClip = AudioClip.Create(trimmedClip.name + "_proc", micPosition, trimmedClip.channels, trimmedClip.frequency, false);
        processedClip.SetData(processedSamples, 0);

        // use processed clip for analysis / upload, keep playBackClip unchanged
        this.clip = processedClip;

        // Parallel API calls
        bool azureDone = false;
        this.ttsDone = false;
        string azureTranscript = null;
        string azureError = null, ttsError = null;
        this.ttsFailure = false;

        // Start Azure STT
        StartCoroutine(SendAudioToAzureApi(null, this.clip,
            (transcript) => {
                azureTranscript = transcript;
                azureDone = true;
            },
            (error) => {
                azureError = error;
                azureDone = true;
            }
        ));

        // Start TTS recognition
        StartCoroutine(UploadAudioFileServer(null, this.clip,
            (response) => {
                LogController.Instance.debug($"Start to pass to recognition request");
            },
            (error) => {
                ttsError = error;
                this.ttsDone = true;
            }
        ));

        // Wait for both to finish
        while (!azureDone || !this.ttsDone)
            yield return null;

        if (ttsError != null && azureError != null)
        {
            this.UpdateUI($"Both recognitions failed.\nAzure error: {azureError}\nTTS error: {ttsError}");
            this.switchPage(Stage.Record);
        }
        else
        {
            // Final UI/page update
            if (!this.ttsFailure)
            {
                this.UpdateUI("TTS recognition passed.");
            }
            else
            {
                this.UpdateUI("TTS failed, fallback to Azure STT result.");
                this.switchPage(Stage.PlaybackResult);
                var playerController = this.GetComponent<PlayerController>();
                if (playerController != null)
                {
                    playerController.submitAnswer(this.answerText.text);
                }
            }
        }
    }

    public void StartPlayback()
    {
        if(this.isPlaying && this.playbackButton.texture == this.playbackBtnTexs[1])
        {
            this.PausePlayback();
        }
        else
        {
            if (this.playBackClip == null)
            {
                this.UpdateUI("No recording available for playback.");
                return;
            }
            if (playbackSource == null)
            {
                playbackSource = gameObject.AddComponent<AudioSource>();
            }

#if UNITY_EDITOR
            playbackSource.outputAudioMixerGroup = this.recordingMixerGroup;
#endif
            playbackSource.clip = this.playBackClip;
            playbackSource.loop = false;
            playbackSource.volume = 1.0f;

            if (Mathf.Approximately(playbackSource.time, playbackSource.clip.length - 0.01f))
            {
                playbackSource.time = 0f;
            }

            playbackSource.Play();

            this.isPlaying = true;
            this.playbackButton.texture = this.playbackBtnTexs[1];
        }
    }

    public void PausePlayback()
    {
        if (playbackSource != null && playbackSource.isPlaying)
        {
            playbackSource.Pause();
            this.playbackButton.texture = this.playbackBtnTexs[0];
        }

        this.isPlaying = false;
    }

    public void StopPlayback()
    {
        if (playbackSource != null && playbackSource.isPlaying)
        {
            playbackSource.Stop();
        }
        this.isPlaying = false;

        if (this.playbackSlider != null)
        {
            this.playbackSlider.value = 0;
        }
        this.playbackButton.texture = this.playbackBtnTexs[0];
        if (this.playbackText != null) this.playbackText.text = "00:00:00";
    }

    private void OnSliderValueChanged(float value)
    {
        if (playbackSource != null && playbackSource.clip != null && !isRecording)
        {
            // Clamp the slider value to ensure it stays within the valid range
            value = Mathf.Clamp(value, 0f, 1f);

            // Calculate the playback position in seconds
            if (!playbackSource.isPlaying)
            {
                float playbackPosition = value * playbackSource.clip.length;
                playbackSource.Play();
                playbackSource.Pause();
                playbackSource.time = Mathf.Clamp(playbackPosition, 0f, playbackSource.clip.length - 0.01f);
            }
            this.playbackButton.texture = this.playbackBtnTexs[playbackSource.isPlaying ? 1 : 0];
            this.updatePlayBackText(playbackSource.time);
        }
    }

    private void updatePlayBackText(float playBackTime)
    {
        if (this.playbackText != null)
        {
            int totalSeconds = Mathf.FloorToInt(playBackTime);
            TimeSpan timeSpan = TimeSpan.FromSeconds(totalSeconds);
            this.playbackText.text = timeSpan.ToString(@"hh\:mm\:ss");
        }
    }

    private IEnumerator SendAudioToAzureApi(byte[] _wavData, AudioClip audioClip, Action<string> onSuccess, Action<string> onError)
    {
        if (this.answerText != null) this.answerText.text = "";
        this.UpdateUI("Processing audio...");
        byte[] wavData = (_wavData != null) ? _wavData : ConvertAudioClipToWav(audioClip);

        if (wavData == null)
        {
            this.UpdateUI("Failed to convert audio to WAV.");
            yield break;
        }

        WWWForm form = new WWWForm();
        string fileName = $"audio_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
        form.AddField("api", "ROMediaLibrary.getSttFromWav");
        form.AddBinaryData("file", wavData, fileName, "audio/wav");
        form.AddField("json", "[\"en-GB\"]");

        this.ApiUrl = $"https://{LoaderConfig.Instance.CurrentHostName}/RainbowOne/index.php/PHPGateway/proxy/2.8/";
        LogController.Instance.debug($"SendAudioToAzureApi: {this.ApiUrl}");
        UnityWebRequest request = UnityWebRequest.Post(this.ApiUrl, form);
        string jwtToken = string.IsNullOrEmpty(LoaderConfig.Instance.apiManager.jwt) ? this.JwtToken : LoaderConfig.Instance.apiManager.jwt;
        request.SetRequestHeader("Authorization", $"Bearer {jwtToken}");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            try
            {
                // Parse the JSON response
                this.sttResponse = JsonUtility.FromJson<SttResponse>(responseText);
                LogController.Instance.debug($"STT Response: {responseText}");
                if (this.sttResponse.data != null && this.sttResponse.data.Length > 0)
                {

                    StringBuilder transcriptBuilder = new StringBuilder();
                    foreach (var result in this.sttResponse.data)
                    {
                        if (!string.IsNullOrWhiteSpace(result.transcript))
                        {
                            transcriptBuilder.Append(result.transcript.Trim());
                            transcriptBuilder.Append(" ");
                        }
                    }
                    string combinedTranscript = transcriptBuilder.ToString().Trim();
                    if (this.answerText != null && string.IsNullOrEmpty(this.answerText.text))
                        this.answerText.text = combinedTranscript;

                    onSuccess.Invoke(combinedTranscript);
                }
                else
                {
                    this.UpdateUI("No transcription data available.");
                    onError.Invoke("No transcription data response.");
                }
            }
            catch (Exception ex)
            {
                LogController.Instance.debugError($"Error parsing JSON: {ex.Message}");
                this.UpdateUI("Error processing transcription response.");
                onError.Invoke("Error processing transcription response.");
            }
        }
        else
        {
            this.UpdateUI($"Error: {request.error}");
            onError.Invoke("Error processing response.");
        }
    }

    public IEnumerator UploadAudioFileServer(byte[] _wavData,
                                        AudioClip audioClip, 
                                       Action<string> onSuccess, 
                                       Action<string> onError, 
                                       int retryCount = 5, 
                                       float retryDelay = 2f)
    {
        string uploadUrl = $"https://{LoaderConfig.Instance.CurrentHostName}/RainbowOne/index.php/transport/Slave/upload/2";
        LogController.Instance.debug($"Upload URL: {uploadUrl}");
        //roWeb upload structure:
        /*if (["pdf", "doc", "docx"].indexOf(extension) != -1)
        {
            options = { fileType: "file" };
            uploadType = "DOC";
        }
        else if (["mp3", "m4a", "wav"].indexOf(extension) != -1)
        {
            options = { fileType: "audio" };
            uploadType = 2;
        }
        else if (["jpg", "jpeg", "png", "gif"].indexOf(extension) != -1)
        {
            options = { fileType: "image" };
            uploadType = 1;
        }
        else if (["mp4", "mov"].indexOf(extension) != 1)
        {
            options = { fileType: "video" };
            uploadType = 3;
        }*/
        byte[] audioData = (_wavData != null) ? _wavData : ConvertAudioClipToWav(audioClip);

        if (audioData == null)
        {
            this.UpdateUI("Failed to convert audio to WAV.");
            onError?.Invoke("Failed to convert audio to WAV.");
            yield break;
        }

        int attempt = 0;
        while (attempt < retryCount)
        {
            attempt++;
            WWWForm form = new WWWForm();
            string fileName = $"audio_{DateTime.Now:yyyyMMdd_HHmmss}";
            string file = fileName + ".wav";
            form.AddField("Filename", fileName);
            form.AddBinaryData("Filedata", audioData, file, "audio/wav");

            UnityWebRequest request = UnityWebRequest.Post(uploadUrl, form);
            if (!string.IsNullOrEmpty(this.JwtToken))
            {
                request.SetRequestHeader("Authorization", $"Bearer {this.JwtToken}");
            }
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                LogController.Instance.debug("File uploaded successfully.");
                var uploadResponse = JsonUtility.FromJson<UploadResponse>(request.downloadHandler.text);
                LogController.Instance.debug("Upload Response: " + request.downloadHandler.text);
                if (!string.IsNullOrEmpty(uploadResponse.url))
                {
                    string audioUrl = "";
                    if (LoaderConfig.Instance.currentHostName != HostName.prod)
                    {
                        audioUrl = "//" + LoaderConfig.Instance.CurrentHostName + uploadResponse.url.Replace("\\", "");
                    }
                    else
                    {
                        audioUrl = uploadResponse.url;
                    }
                    LogController.Instance.debug("uploadResponse url : " + audioUrl);
                    onSuccess?.Invoke("Success to pass to recognition request.");
                    yield return SendAudioTTSRecognitionRequest(
                        audioUrl,
                        this.TextToRecognize
                    );
                    yield break;
                }
                else
                {
                    LogController.Instance.debugError("Failed to extract audio URL from upload response." + uploadResponse);
                    this.UpdateUI("Failed to extract audio URL from upload response.");
                    onError?.Invoke("Failed to extract audio URL from upload response.");
                    yield break;
                }
            }
            else
            {
                LogController.Instance.debugError($"File upload failed (attempt {attempt}): {request.error}");
                if (this.loadingTextCoroutine != null)
                {
                    StopCoroutine(this.loadingTextCoroutine);
                    this.loadingTextCoroutine = null;
                    SetUI.Set(this.processButtonCg, false);
                }
                this.UpdateUI($"Upload failed (attempt {attempt}/{retryCount}): {request.error}");
                if(this.submitAudioText != null) this.submitAudioText.text = $"Retry Upload ({attempt}/{retryCount})";
                if (attempt < retryCount)
                    yield return new WaitForSeconds(retryDelay);
            }
        }

        // All attempts failed
        this.UpdateUI("Upload failed after multiple attempts. Please check your network and try again.");
        onError?.Invoke("Upload failed after multiple attempts.");
    }

    private IEnumerator SendAudioTTSRecognitionRequest(string audioUrl, string textToRecognize="", string language= "en-US", string purpose= "enSpeech")
    {
        if (string.IsNullOrEmpty(audioUrl))
        {
            this.UpdateUI("audioUrl is null. Cannot process.");
            yield break;
        }
        yield return new WaitForSeconds(1f);
        this.UpdateUI("Converting AudioClip to binary data...");

        this.UpdateUI("Sending audio recognition request...");

        string jsonPayload = "";
        WWWForm form = new WWWForm();
        string fieldValue = "";

        if(this.detectMethod != DetectMethod.prompt) { 
            if (LoaderConfig.Instance.currentHostName != HostName.prod)
            {
                fieldValue = "ROSpeechRecognition.test_recognize_tts";
            }
            else
            {
                fieldValue = "ROSpeechRecognition.recognize_tts";
            }
            jsonPayload = $"[\"{audioUrl}\",\"{textToRecognize}\",\"{language}\",\"{purpose}\"]";

            if (LoaderConfig.Instance.currentHostName == HostName.prod)
                this.ApiUrl = $"https://{LoaderConfig.Instance.CurrentHostName}/RainbowOne/index.php/PHPGateway/proxy/2.8/";
            else
                this.ApiUrl = $"https://uat.starwishparty.com/RainbowOne/index.php/PHPGateway/proxy/2.8/";
        }
        else
        {
            fieldValue = "SpeechPractice.gptTranscribe";
            jsonPayload = $"[\"{audioUrl}\",\"{textToRecognize}\"]";

            if (LoaderConfig.Instance.currentHostName == HostName.prod)
                this.ApiUrl = $"https://{LoaderConfig.Instance.CurrentHostName}/RainbowOne/index.php/PHPGateway/proxy/2.8/";
            else
                this.ApiUrl = $"https://dev.openknowledge.hk/RainbowOne/index.php/PHPGateway/proxy/2.8/";
        }

        form.AddField("api", fieldValue);
        form.AddField("json", jsonPayload);

        UnityWebRequest request = UnityWebRequest.Post(this.ApiUrl, form);
        request.SetRequestHeader("Authorization", $"Bearer {this.JwtToken}");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            LogController.Instance.debug($"responseText: {responseText}");
                try
                {
                    if(this.detectMethod == DetectMethod.prompt)
                    {
                        var gptTranscribeResponse = JsonUtility.FromJson<GPTTranscribeResponse>(responseText);                       
                        if(gptTranscribeResponse != null && gptTranscribeResponse.result != null)
                        {
                        StringBuilder result = new StringBuilder();
                        this.gptTranscribeResult = gptTranscribeResponse.result;
                        string transcript = this.gptTranscribeResult.text;
                        result.AppendLine($"DisplayText: {transcript}");
                        this.UpdateUI(result.ToString());
                        if (this.answerText != null) this.answerText.text = transcript;
                        this.switchPage(Stage.PlaybackResult);

                        if (transcript != QuestionController.Instance.currentQuestion.correctAnswer)
                            this.ttsFailure = true;

                        var playerController = this.GetComponent<PlayerController>();
                        if (playerController != null)
                        {
                            if (!this.ttsFailure)
                            {
                                if(this.accurancyTitle != null && this.accurancyText != null) { 
                                   this.accurancyTitle.text = "Correct";
                                   this.accurancyText.text = "Answer";
                                   this.accurancyText.resizeTextMaxSize = 30;
                                }
                                transcript = QuestionController.Instance.currentQuestion.fullSentence;
                            }
                            else
                            {
                                transcript = "";
                            }

                            playerController.submitAnswer(transcript, () =>
                                  {
                                      this.showCorrectSentence(
                                          QuestionController.Instance.currentQuestion.fullSentence,
                                          null);
                                  }
                             );
                        }
                        
                    }
                    }
                    else
                    {
                        // Parse the root JSON structure
                        var recognitionResponse = JsonUtility.FromJson<RecognitionResponse>(responseText);
                        if (recognitionResponse != null && recognitionResponse.result != null)
                        {
                            this.recognitionResult = recognitionResponse.result;

                            NBest[] Best = recognitionResult.NBest;
                            StringBuilder result = new StringBuilder();
                            string transcript = "";
                            string displayText = "";
                            string correctAnswer = recognitionResponse.result.DisplayText;
                            float averageScore = 0f;
                            this.wordDetails = null;
                            // Log the NBest array
                            if (Best != null)
                            {
                                foreach (var nBest in Best)
                                {
                                    averageScore = (nBest.AccuracyScore + nBest.PronScore + nBest.FluencyScore + nBest.CompletenessScore) / 4f;
                                    result.AppendLine($"Score: {nBest.PronScore}");
                                    this.wordDetails = nBest.Words;
                                    this.checkSpeech(this.wordDetails, result);

                                    if (this.accurancyText != null)
                                        this.accurancyText.text = $"{averageScore}%"; //Rating

                                    if (averageScore <= ((this.passAccuracyScore + this.passPronScore) / 2f))
                                    {
                                        this.ttsFailure = true;
                                    }
                                }
                                this.ttsDone = true;

                                if (!this.ttsFailure)
                                {
                                    transcript = correctAnswer;
                                    string correctAns = QuestionController.Instance.currentQuestion.fullSentence;
                                    displayText = Regex.Replace(transcript, @"[^\w\s]", "").ToLower();
                                    result.AppendLine($"DisplayText: {displayText}");
                                    //transcript = Regex.Replace(recognitionResult.DisplayText, @"[^\w\s]", "").ToLower();
                                    this.UpdateUI(result.ToString());
                                    if (this.answerText != null) this.answerText.text = correctAns;
                                    this.switchPage(Stage.PlaybackResult);

                                    var playerController = this.GetComponent<PlayerController>();
                                    if (playerController != null)
                                    {
                                        playerController.submitAnswer(
                                            correctAns,
                                            () =>
                                            {
                                                this.showCorrectSentence(
                                                    QuestionController.Instance.currentQuestion.fullSentence,
                                                    this.wordDetails);
                                            }
                                         );
                                    }
                                }
                            }
                        }
                        else
                        {
                            LogController.Instance.debugError("Failed to parse recognition result.");
                            this.UpdateUI("Failed to parse recognition result.");
                        }
                }
            }
            catch (Exception ex)
            {
                LogController.Instance.debugError($"Error parsing API response: {ex.Message}");
                this.UpdateUI("Error processing audio recognition response.");
                if (this.accurancyText != null) this.accurancyText.text = $"Rating: {0}%";
                if (this.answerText != null) this.answerText.text = "";
                var playerController = this.GetComponent<PlayerController>();
                SetUI.Set(this.playBackStatusText, true);
                var progressText = this.playBackStatusText.GetComponentInChildren<TextMeshProUGUI>();  
                if (progressText != null) 
                    progressText.text = "Upload Error.";
                this.switchPage(Stage.PlaybackResult);
                if (playerController != null)
                {
                    playerController.submitAnswer("");
                }
            }
        }
        else
        {
            this.UpdateUI($"Error: {request.error}");
        }
    }

    public void showCorrectSentence(string displayText, WordDetail[] wordDetails = null)
    {
        var currentQuestion = QuestionController.Instance.currentQuestion;
        if (currentQuestion.underlineWordRecordIcon != null) currentQuestion.underlineWordRecordIcon.SetActive(false);
        TextMeshProUGUI questionTextpro = currentQuestion.QuestionTexts[0];

        int textCount = currentQuestion.QuestionTexts.Length;
        displayText = (displayText ?? "").TrimStart();

        // Split the sentence into words (preserve punctuation if needed)
        string[] sentenceWords = displayText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        // Detect InsertWord question and extract the inserted word (e.g. "(unless)")
        string insertedWord = null;
        try
        {
            if (currentQuestion.qa != null &&
                !string.IsNullOrEmpty(currentQuestion.qa.questionType) &&
                currentQuestion.qa.questionType.Equals("InsertWord", StringComparison.OrdinalIgnoreCase))
            {
                // try explicit field first if present in JSON->QuestionList (not guaranteed)
                // fallback to parentheses extraction from the question text
                if (!string.IsNullOrEmpty(currentQuestion.qa.checkSingleWord))
                {
                    // no-op: checkSingleWord used elsewhere; kept for compatibility
                }

                var q = currentQuestion.qa.question ?? "";
                var m = Regex.Match(q, @"\(([^)]+)\)");
                if (m.Success)
                {
                    insertedWord = m.Groups[1].Value.Trim();
                }
                else
                {
                    // Fallback: try to infer by comparing fullSentence and question (remove punctuation)
                    try
                    {
                        var full = Regex.Replace(currentQuestion.qa.fullSentence ?? "", @"[^\w\s]", "").ToLowerInvariant();
                        var wrong = Regex.Replace(q ?? "", @"[^\w\s]", "").ToLowerInvariant();
                        var fullWords = full.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var wrongWords = wrong.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < fullWords.Length; i++)
                        {
                            if (i >= wrongWords.Length || fullWords[i] != wrongWords[i])
                            {
                                insertedWord = fullWords[i];
                                break;
                            }
                        }
                    }
                    catch { insertedWord = null; }
                }
                if (!string.IsNullOrEmpty(insertedWord))
                    insertedWord = Regex.Replace(insertedWord, @"[^\w]", "");
            }
        }
        catch (Exception ex)
        {
            LogController.Instance?.debugError("InsertWord detection failed: " + ex.Message);
            insertedWord = null;
        }

        // --- build normalized list of underlined segments from currentQuestion.displayQuestion (in order) ---
        var underlinedSegments = new List<string>();
        if (string.IsNullOrEmpty(insertedWord)) // skip parsing underlines for InsertWord (we use insertedWord)
        {
            try
            {
                string displayQuestionMarkup = currentQuestion.displayQuestion ?? "";
                var matches = Regex.Matches(displayQuestionMarkup, @"<u\b[^>]*>(.*?)<\/u>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                foreach (Match m in matches)
                {
                    string inner = m.Groups[1].Value ?? "";
                    // Remove nested tags like <color=...>...</color>
                    string withoutTags = Regex.Replace(inner, "<.*?>", "");
                    // Remove placeholder underscores or invisible color padding
                    withoutTags = withoutTags.Replace("_", "");
                    // Normalize: remove non-word chars and lowercase
                    string normalized = Regex.Replace(withoutTags, @"[^\w]", "").ToLowerInvariant();
                    if (!string.IsNullOrEmpty(normalized))
                        underlinedSegments.Add(normalized);
                }
            }
            catch (Exception ex)
            {
                LogController.Instance.debugError("Error parsing displayQuestion underlines: " + ex.Message);
            }
        }

        // If no underlined segments found, fallback to previous behavior: highlight only by correctAnswer membership
        var correctAnswerWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string correctAnswer = currentQuestion.correctAnswer ?? "";
            var parts = correctAnswer.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                string normalized = Regex.Replace(p, @"[^\w]", "").ToLowerInvariant();
                if (!string.IsNullOrEmpty(normalized))
                    correctAnswerWords.Add(normalized);
            }
        }
        catch (Exception ex)
        {
            LogController.Instance.debugError("Error building correct answer words: " + ex.Message);
        }

        // Helper: normalize a single word
        string NormalizeWord(string w) => Regex.Replace(w ?? "", @"[^\w]", "").ToLowerInvariant();

        // Mark which word indices correspond to underlined segments (sequence-aware)
        bool[] highlightFlags = new bool[sentenceWords.Length];

        // --- NEW: For SentenceCorrect questions prefer token-diff mapping between wrong and full sentence ---
        bool usedDiffIndex = false;
        try
        {
            if (currentQuestion.qa != null &&
                !string.IsNullOrEmpty(currentQuestion.qa.question) &&
                currentQuestion.qa.questionType != null &&
                currentQuestion.qa.questionType.Equals("SentenceCorrect", StringComparison.OrdinalIgnoreCase))
            {
                var wrongMatches = Regex.Matches(currentQuestion.qa.question ?? "", @"\S+");
                var fullMatches = Regex.Matches(displayText ?? "", @"\S+");

                int diffIndex = -1;
                int minLen = Math.Min(wrongMatches.Count, fullMatches.Count);

                for (int t = 0; t < minLen; t++)
                {
                    string wToken = Regex.Replace(wrongMatches[t].Value, @"[^\w]", "").ToLower();
                    string fToken = Regex.Replace(fullMatches[t].Value, @"[^\w]", "").ToLower();
                    if (!string.Equals(wToken, fToken, StringComparison.Ordinal))
                    {
                        diffIndex = t;
                        break;
                    }
                }

                if (diffIndex == -1 && wrongMatches.Count != fullMatches.Count)
                {
                    diffIndex = minLen;
                }

                if (diffIndex >= 0 && diffIndex < sentenceWords.Length)
                {
                    highlightFlags[diffIndex] = true;
                    usedDiffIndex = true;
                }
            }
        }
        catch (Exception ex)
        {
            LogController.Instance.debugError("SentenceCorrect diff mapping failed: " + ex.Message);
            usedDiffIndex = false;
        }

        if (string.IsNullOrEmpty(insertedWord) && underlinedSegments.Count > 0 && sentenceWords.Length > 0 && !usedDiffIndex)
        {
            int searchStart = 0;
            for (int s = 0; s < underlinedSegments.Count; s++)
            {
                string seg = underlinedSegments[s];
                bool matchedSegment = false;

                for (int start = searchStart; start < sentenceWords.Length; start++)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int end = start; end < sentenceWords.Length; end++)
                    {
                        sb.Append(NormalizeWord(sentenceWords[end]));
                        var combined = sb.ToString();

                        if (combined.Length < seg.Length)
                            continue;

                        if (combined.Length > seg.Length)
                            break; // overshot, try next start

                        if (combined == seg)
                        {
                            // mark indices [start..end] as highlighted
                            for (int k = start; k <= end; k++)
                                highlightFlags[k] = true;
                            searchStart = end + 1;
                            matchedSegment = true;
                            break;
                        }
                    }
                    if (matchedSegment) break;
                }
            }
        }

        // If we have an insertedWord explicitly, mark occurrences to highlight only that word
        if (!string.IsNullOrEmpty(insertedWord))
        {
            string normalizedInserted = NormalizeWord(insertedWord);
            for (int idx = 0; idx < sentenceWords.Length; idx++)
            {
                if (NormalizeWord(sentenceWords[idx]) == normalizedInserted)
                    highlightFlags[idx] = true;
            }
        }

        for (int i = 0; i < textCount; i++)
        {
            TextMeshProUGUI textpro = currentQuestion.QuestionTexts[i];
            if (textpro != null)
            {
                bool markerText = textpro.gameObject.name == "MarkerText";
                var result = new StringBuilder();

                for (int idx = 0; idx < sentenceWords.Length; idx++)
                {
                    string word = sentenceWords[idx];
                    WordDetail wordDetail = null;
                    string errorType = null;
                    bool isError = false;

                    if (wordDetails != null)
                    {
                        // Find the WordDetail for this word (case-insensitive, ignore punctuation)
                        string cleanWord = Regex.Replace(word, @"[^\w]", "");
                        wordDetail = Array.Find(wordDetails, wd =>
                            string.Equals(wd.Word, cleanWord, StringComparison.OrdinalIgnoreCase));
                        if (wordDetail != null)
                        {
                            errorType = wordDetail.ErrorType;
                            isError = !string.IsNullOrEmpty(errorType) && errorType != "None";
                        }
                    }

                    if (isError)
                    {
                        LogController.Instance.debug("result: " + errorType);
                        switch (errorType)
                        {
                            case "Mispronunciation":
                                if (markerText)
                                    result.Append($"<mark=#FFFF00 padding='0,12,5,10'>{word}</mark> ");
                                else
                                    result.Append($"{word} ");
                                break;
                            case "Omission":
                                if (markerText)
                                    result.Append($"<mark=#EF9E98 padding='0,12,5,10'>{word}</mark> ");
                                else
                                    result.Append($"<color=red>{word}</color> ");
                                break;
                            case "Insertion":
                            case "Substitution":
                                result.Append($"<u>{word}</u> ");
                                break;
                            default:
                                result.Append($"{word} ");
                                break;
                        }
                    }
                    else
                    {
                        // Only highlight when this exact word occurrence is matched to an underlined segment or insertedWord.
                        if (markerText && highlightFlags[idx])
                        {
                            result.Append($"<mark=#A6E32A padding='0,12,5,10'>{word}</mark> ");
                        }
                        else
                        {
                            // Fallback: if no underlined segments detected at all, use correctAnswer membership to highlight
                            if (underlinedSegments.Count == 0 && string.IsNullOrEmpty(insertedWord))
                            {
                                string cleanWordForCheck = NormalizeWord(word);
                                if (markerText && correctAnswerWords.Contains(cleanWordForCheck))
                                    result.Append($"<mark=#A6E32A padding='0,12,5,10'>{word}</mark> ");
                                else
                                    result.Append($"{word} ");
                            }
                            else
                            {
                                result.Append($"{word} ");
                            }
                        }
                    }
                }

                textpro.text = result.ToString().TrimEnd();
            }
        }
    }


    private byte[] ConvertAudioClipToWav(AudioClip audioClip)
    {
        if (audioClip == null) return null;

        using (MemoryStream stream = new MemoryStream())
        {
            int sampleCount = audioClip.samples * audioClip.channels;
            int frequency = audioClip.frequency;

            // Write WAV header
            WriteWavHeader(stream, sampleCount, frequency, audioClip.channels);

            // Write audio data
            float[] samples = new float[sampleCount];
            audioClip.GetData(samples, 0);

            foreach (float sample in samples)
            {
                short intSample = (short)(sample * short.MaxValue);
                stream.WriteByte((byte)(intSample & 0xFF));
                stream.WriteByte((byte)((intSample >> 8) & 0xFF));
            }

            return stream.ToArray();
        }
    }

    private void WriteWavHeader(Stream stream, int sampleCount, int frequency, int channels)
    {
        int byteRate = frequency * channels * 2;

        stream.Write(Encoding.ASCII.GetBytes("RIFF"), 0, 4);
        stream.Write(BitConverter.GetBytes(36 + sampleCount * 2), 0, 4);
        stream.Write(Encoding.ASCII.GetBytes("WAVE"), 0, 4);
        stream.Write(Encoding.ASCII.GetBytes("fmt "), 0, 4);
        stream.Write(BitConverter.GetBytes(16), 0, 4);
        stream.Write(BitConverter.GetBytes((short)1), 0, 2);
        stream.Write(BitConverter.GetBytes((short)channels), 0, 2);
        stream.Write(BitConverter.GetBytes(frequency), 0, 4);
        stream.Write(BitConverter.GetBytes(byteRate), 0, 4);
        stream.Write(BitConverter.GetBytes((short)(channels * 2)), 0, 2);
        stream.Write(BitConverter.GetBytes((short)16), 0, 2);
        stream.Write(Encoding.ASCII.GetBytes("data"), 0, 4);
        stream.Write(BitConverter.GetBytes(sampleCount * 2), 0, 4);
    }

    /*public void UpdateButtonStates()
    {
        this.stopRecordText.GetComponent<CanvasGroup>().alpha = isRecording ? 1f : 0f;
        SetUI.Set(this.remindRecordTip, !isRecording, 0.5f);
        if (this.recordButton) this.recordButton.SetActive(!isRecording);
        if (this.stopButton) this.stopButton.SetActive(isRecording);
        if (this.playbackButton) this.playbackButton.gameObject.SetActive(!isRecording && clip != null);
    }*/

    private void UpdateUI(string message)
    {
        if (debugText != null)
        {
            debugText.text = message;
        }
    }

    public void NextQuestion()
    {
        GameController.Instance?.UpdateNextQuestion();
        var playerController = this.GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.resetRetryTime();
        }
        this.switchPage(Stage.Record); // reset
    }

    public void ShowDirectCorrectAnswer()
    {
        var questionType = QuestionController.Instance.currentQuestion.questiontype;

        switch (questionType)
        {
            case QuestionType.Audio:
                this.showCorrectSentence(QuestionController.Instance.currentQuestion.correctAnswer, this.wordDetails);
                break;
            case QuestionType.InsertWord:
                this.showCorrectSentence(
                    QuestionController.Instance.currentQuestion.qa.insertWord, 
                    this.wordDetails);
                break;
            case QuestionType.SentenceCorrect:
            case QuestionType.FillInBlank:
                this.showCorrectSentence(QuestionController.Instance.currentQuestion.fullSentence, this.wordDetails);
                break;
        }

        if (this.accurancyText != null)
            this.accurancyText.text = $"Word missing.";
    }

    public void ResetRecorder()
    {
        this.playBackClip = null;
        this.clip = null;
        GameController.Instance?.setGetScorePopup(false);
        GameController.Instance?.setWrongPopup(false);
        this.ttsDone = false;
        this.ttsFailure = false;
        this.hasErrorWord = false;
        SetUI.Set(this.playBackStatusText, false);
        SetUI.Set(this.processButtonCg, false);
        AudioController.Instance?.fadingBGM(true, 1f);
        this.playbackButton.texture = this.playbackBtnTexs[0];
        //this.ShowDirectCorrectAnswer(false);
        if (this.stopRecordText != null) this.stopRecordText.text = "00:00:00";
        if (this.playbackText != null) this.playbackText.text = "00:00:00";
        this.isRecording = false;
        this.recordingTime = 0f;
        //this.UpdateButtonStates();
        SetUI.SetGroup(this.pages, 0);
        this.controlResultPage(-1);

        if (this.loadingTextCoroutine != null)
        {
            StopCoroutine(this.loadingTextCoroutine);
            this.loadingTextCoroutine = null;
        }
        if (this.submitAudioText != null) this.submitAudioText.text = "Listening...";
        SetUI.Set(this.stopButton, false, 0f, 0.5f);
    }
}

[Serializable]
public class UploadResponse
{
    public string url;
    public string path;
    public int id;
    public string key;
    public string token;
    public string filename;
    public string checksum;
    public int len;
    public int log_id;
    public int server;
    public int code;
}

[Serializable]
public class GPTTranscribeResponse
{
    public int code;
    public GPTTranscribeResult result;
    public string path;
}

[Serializable]
public class GPTTranscribeResult
{
    public string text;
    public Usage usage;
}

[Serializable]
public class Usage
{
    public string type;
    public int total_tokens;
    public int input_tokens;
    public Input_token_details input_token_details;
    public int output_tokens;
}

[Serializable]
public class Input_token_details
{
    public int text_tokens;
    public int audio_tokens;
}


[Serializable]
public class RecognitionResponse
{
    public int code;
    public RecognitionResult result;
    public int version;
}

[Serializable]
public class RecognitionResult
{
    public string RecognitionStatus;
    public float Offset;
    public float Duration;
    public string DisplayText;
    public float SNR;
    public NBest[] NBest;
}

[Serializable]
public class NBest
{
    public float Confidence;
    public string Lexical;
    public string ITN;
    public string MaskedITN;
    public string Display;
    public int AccuracyScore;
    public int FluencyScore;
    public int CompletenessScore;
    public float PronScore;
    public WordDetail[] Words;
}

[Serializable]
public class WordDetail
{
    public string Word;
    public float Offset;
    public float Duration;
    public float Confidence;
    public int AccuracyScore;
    public string ErrorType;
    public SyllableDetail[] Syllables;
    public PhonemeDetail[] Phonemes;
}

[Serializable]
public class SyllableDetail
{
    public string Syllable;
    public string Grapheme;
    public float Offset;
    public float Duration;
    public int AccuracyScore;
}

[Serializable]
public class PhonemeDetail
{
    public string Phoneme;
    public float Offset;
    public float Duration;
    public int AccuracyScore;
}

[Serializable]
public class SttResponse
{
    public SttData[] data;
}

[Serializable]
public class SttData
{
    public string transcript;
    public float confidence;
    public WordData[] words;
}

[Serializable]
public class WordData
{
    public string word;
    public string startTime;
    public string endTime;
}

