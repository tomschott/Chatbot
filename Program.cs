using System;
using Microsoft.CognitiveServices.Speech;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Transcription;

class Program
{
    private static string AZURE_SPEECH_KEY = Environment.GetEnvironmentVariable("SPEECH_KEY");
    private static string AZURE_REGION = Environment.GetEnvironmentVariable("SPEECH_REGION");
    private static string AZURE_OPENAI_KEY = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
    private static string AZURE_OPENAI_ENDPOINT = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");

    // Track if the bot is speaking
    private static bool isBotSpeaking = false;
    private static SpeechSynthesizer botSynthesizer = null;

    async static Task Main(string[] args)
    {
        var speechConfig = SpeechConfig.FromSubscription(AZURE_SPEECH_KEY, AZURE_REGION);
        speechConfig.SpeechRecognitionLanguage = "en-US";
        speechConfig.SetProperty(PropertyId.SpeechServiceResponse_DiarizeIntermediateResults, "true");

        var stopRecognition = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var audioConfig = AudioConfig.FromDefaultMicrophoneInput())
        using (var conversationTranscriber = new ConversationTranscriber(speechConfig, audioConfig))
        {
            conversationTranscriber.Transcribing += async (s, e) =>
            {
                Console.WriteLine($"TRANSCRIBING: Text={e.Result.Text} Speaker ID={e.Result.SpeakerId}");

                // If guest is speaking and the bot is speaking, stop the bot's speech
                if (e.Result.SpeakerId == "Guest-1" && isBotSpeaking)
                {
                    Console.WriteLine("Guest started speaking, stopping bot response...");
                    await StopBotSpeechAsync();
                }
            };

            conversationTranscriber.Transcribed += async (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    Console.WriteLine($"TRANSCRIBED: Text={e.Result.Text} Speaker ID={e.Result.SpeakerId}");
                    
                    if (e.Result.SpeakerId == "Guest-1") // Assuming "User" is the guest ID
                    {
                        // Send transcription to Azure OpenAI GPT-4 Mini for chatbot response
                        string response = await GetAzureOpenAIResponseAsync(e.Result.Text);

                        if (isBotSpeaking) {
                            Console.WriteLine("Guest is finnished speaking, stopping bot response...");
                            await StopBotSpeechAsync();
                        }

                        Console.WriteLine($"Chatbot Response: {response}");

                        // Synthesize the chatbot's text response to speech
                        await SynthesizeSpeechAsync(response);
                    }
                }
                else if (e.Result.Reason == ResultReason.NoMatch)
                {
                    Console.WriteLine("NOMATCH: Speech could not be transcribed.");
                }
            };

            conversationTranscriber.Canceled += (s, e) =>
            {
                Console.WriteLine($"CANCELED: Reason={e.Reason}");
                stopRecognition.TrySetResult(0);
            };

            conversationTranscriber.SessionStopped += (s, e) =>
            {
                Console.WriteLine("Session stopped event.");
                stopRecognition.TrySetResult(0);
            };

            await conversationTranscriber.StartTranscribingAsync();
            Task.WaitAny(new[] { stopRecognition.Task });
            await conversationTranscriber.StopTranscribingAsync();
        }
    }

    public static async Task<string> GetAzureOpenAIResponseAsync(string userInput)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("api-key", AZURE_OPENAI_KEY);

            var message = new
            {
                model = "gpt-4o-mini",
                messages = new List<Dictionary<string, string>>()
                {
                    new Dictionary<string, string>
                    {
                        {"role", "system"},
                        {"content", "You are Albert Einstein"}
                    },
                    new Dictionary<string, string>
                    {
                        {"role", "user"},
                        {"content", userInput}
                    }
                }
            };

            var content = new StringContent(JsonConvert.SerializeObject(message), System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{AZURE_OPENAI_ENDPOINT}/openai/deployments/gpt-4o-mini/chat/completions?api-version=2023-03-15-preview", content);
            var responseString = await response.Content.ReadAsStringAsync();
            var responseJson = JObject.Parse(responseString);
            var responseText = responseJson["choices"][0]["message"]["content"].ToString();

            Console.WriteLine($"Azure GPT-4 Response: {responseText}");
            return responseText;
        }
    }

    static async Task SynthesizeSpeechAsync(string text)
    {   
        var speechConfig = SpeechConfig.FromSubscription(AZURE_SPEECH_KEY, AZURE_REGION);
        botSynthesizer = new SpeechSynthesizer(speechConfig);

        isBotSpeaking = true;

        // Start speaking text asynchronously (no need to pass the token here)
        var result = await botSynthesizer.SpeakTextAsync(text);

        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
        {
            Console.WriteLine("Successfully synthesized the speech.");
        }
        else
        {
            Console.WriteLine($"Speech synthesis failed: {result.Reason}");
        }

        isBotSpeaking = false;
        botSynthesizer.Dispose();
        botSynthesizer = null;
    }

    // Stop the bot's speech if it's currently speaking
    static async Task StopBotSpeechAsync()
    {
        if (botSynthesizer != null && isBotSpeaking)
        {
            Console.WriteLine("Stopping bot speech...");
            
            try
            {
                // Stop the speech asynchronously
                await botSynthesizer.StopSpeakingAsync();

                // Wait until speech stops completely (ensuring botSynthesizer is no longer speaking)
                while (isBotSpeaking)
                {
                    await Task.Delay(100);
                }

                Console.WriteLine("Bot speech stopped.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping speech: {ex.Message}");
            }
            finally
            {
                // Dispose only if not null
                //botSynthesizer.Dispose();
                //botSynthesizer = null;
                isBotSpeaking = false;
            }
        }
        else
        {
            Console.WriteLine("Bot is not speaking, or botSynthesizer is null.");
        }
    }
}