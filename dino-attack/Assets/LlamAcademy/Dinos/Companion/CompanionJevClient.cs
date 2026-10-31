using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LlamAcademy.Dinos.Companion
{
    /// Optional localhost adapter. The game remains fully playable when the gateway is disabled or offline.
    public sealed class CompanionJevClient : MonoBehaviour
    {
        [Serializable]
        private sealed class RequestBody
        {
            public string text = string.Empty;
            public CompanionProfileData profile = CompanionProfileData.CreateDefault();
            public CompanionSituation situation = new CompanionSituation();
        }

        [Serializable]
        private sealed class ResponseBody
        {
            // JsonUtility only fills public or explicitly serialized fields.
            public string intent = string.Empty;
            public float confidence = 0f;
            public bool explicitAutonomy = false;
            public bool needsClarification = false;
            public float tacticalUrgency = 0f;
            public string message = string.Empty;
        }

        [SerializeField] private bool UseGateway;
        [SerializeField] private string GatewayUrl = "http://127.0.0.1:8765/jev";
        [SerializeField, Min(1)] private int TimeoutSeconds = 8;
        private int requestGeneration;

        public bool IsPending { get; private set; }
        public string LastStatus { get; private set; } = "offline-local-parser";
        public bool GatewayEnabled { get => UseGateway; set => UseGateway = value; }
        public string Endpoint { get => GatewayUrl; set => GatewayUrl = value; }

        public IEnumerator Request(
            string text,
            CompanionProfileData profile,
            CompanionSituation situation,
            Action<CompanionIntent, string> complete)
        {
            int generation = ++requestGeneration;
            IsPending = true;
            CompanionIntent local = CompanionLocalIntentParser.Parse(text);
            if (!UseGateway)
            {
                yield return null;
                if (generation == requestGeneration) Complete(local, "local-parser", complete);
                yield break;
            }

            string endpoint = GatewayUrl == null ? string.Empty : GatewayUrl.Trim();
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                if (generation == requestGeneration) Complete(local, "invalid-gateway-url", complete);
                yield break;
            }

            RequestBody body = new RequestBody
            {
                text = text ?? string.Empty,
                profile = (profile ?? CompanionProfileData.CreateDefault()).Clone().Sanitize(),
                situation = (situation ?? new CompanionSituation()).Sanitize(),
            };
            string json = JsonUtility.ToJson(body);
            using UnityWebRequest request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Mathf.Clamp(TimeoutSeconds, 1, 30),
            };
            request.SetRequestHeader("Content-Type", "application/json");
            LastStatus = "gateway-pending";
            yield return request.SendWebRequest();
            if (generation != requestGeneration) yield break;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Complete(local, "gateway-error-fallback:" + request.responseCode, complete);
                yield break;
            }

            ResponseBody response = null;
            try { response = JsonUtility.FromJson<ResponseBody>(request.downloadHandler.text); }
            catch (Exception exception) { LastStatus = "gateway-invalid-response:" + exception.GetType().Name; }
            if (response == null || !Enum.TryParse(response.intent, true, out CompanionMode mode))
            {
                Complete(local, "gateway-invalid-intent-fallback", complete);
                yield break;
            }

            CompanionIntent result = new CompanionIntent
            {
                Mode = mode,
                Confidence = response.confidence,
                ExplicitAutonomy = response.explicitAutonomy,
                NeedsClarification = response.needsClarification,
                TacticalUrgency = response.tacticalUrgency,
                Source = "jev-gateway",
                RawText = text ?? string.Empty,
            }.Sanitize();
            Complete(result, "gateway-ok", complete);
        }

        public void CancelPending()
        {
            requestGeneration++;
            IsPending = false;
            LastStatus = "cancelled";
        }

        private void Complete(CompanionIntent intent, string status, Action<CompanionIntent, string> complete)
        {
            IsPending = false;
            LastStatus = status;
            complete?.Invoke((intent ?? new CompanionIntent { NeedsClarification = true }).Sanitize(), status);
        }
    }
}
