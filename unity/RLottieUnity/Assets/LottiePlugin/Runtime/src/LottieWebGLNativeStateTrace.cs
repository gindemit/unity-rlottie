#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace LottiePlugin
{
    // Single browser/main-thread ABI. No Unity API is called by a type initializer.
    internal static class LottieWebGLNativeStateTrace
    {
        internal const uint Abi = 1, HeaderBytes = 128, RecordBytes = 192, Capacity = 64;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct Header
        {
            public uint magic, abiVersion, headerBytes, recordBytes, capacity;
            public uint storedRecords, returnedRecords, callbacksDropped, registrationsRejected;
            public uint callbacksTotal, registrationsTotal, requestsTotal, requestsRejected, eventsIssuedTotal;
            public uint preArmStateDetected, lateArmTotal, unmatchedEventsTotal, identityOverflowTotal, flags;
            public uint armUnityFrame, firstSequence, lastSequence;
            public uint registrationMetadataLossTotal, requestMetadataLossTotal;
            public uint reserved0, reserved1, reserved2, reserved3, reserved4, reserved5, reserved6, reserved7;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct Record
        {
            public uint recordBytes, abiVersion, sequence, registrationUnityFrame, outcome, validFlags, reservedB, animationId;
            public uint generation, registrationAttempt, registrationOrigin, requestSequence, requestUnityFrame, eventToken, eventUnityFrame, callbackSequence;
            public uint textureToken, width, height, pendingBefore, pendingAfter, availableBefore, availableAfter, contextStatus;
            public uint contextMajor, contextMinor, contextLost, isTextureResult, previousTextureRelation, previousUnpackAlignment, pixelUnpackBufferBound, unpackRowLength;
            public uint unpackSkipRows, unpackSkipPixels, reservedC, reservedD, checkedUploadError, checkedUploadErrorValid, restorationCompleted, firstDrainedError;
            public uint drainedErrorCount, reserved0, reserved1, reserved2, reserved3, reserved4, reserved5, reserved6;
        }

        internal struct Identity
        {
            internal uint animationId, generation, textureToken, registrationAttempt, requestSequence, origin;
            internal bool assigned, generationUnavailable;
        }

        private static bool sGateEvaluated, sInitialized, sSelected, sFrameUnavailable;
        private static string sBuildGuid;
        private static uint sAnimationId, sTextureToken, sEventToken = 1;

        internal static bool EnsureInitialized()
        {
            if (sInitialized) return sSelected;
            // These entry points are the existing Unity main-thread construction/pump paths.
            if (!sGateEvaluated)
            {
                sSelected = IsSelected(Application.absoluteURL);
                sGateEvaluated = true;
            }
            if (!sSelected)
            {
                sInitialized = true;
                return false;
            }
            sBuildGuid = Application.buildGUID;
            ValidateLayout(typeof(Header), HeaderBytes);
            ValidateLayout(typeof(Record), RecordBytes);
            CheckStatus(NativeBridge.LottieWebGLTraceArm(Abi, HeaderBytes, RecordBytes, UnityFrame()), "arm");
            sInitialized = true;
            return true;
        }

        internal static bool IsSelected(string url)
        {
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(parsed.UserInfo) ||
                !(string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase) || parsed.Host == "127.0.0.1"))
                return false;
            // Uri canonicalizes abbreviated/numeric IPv4 aliases. Require the literal authority host too.
            int authorityStart = url.IndexOf("://", StringComparison.Ordinal);
            if (authorityStart < 0) return false;
            authorityStart += 3;
            int authorityEnd = url.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
            string authority = authorityEnd < 0 ? url.Substring(authorityStart) : url.Substring(authorityStart, authorityEnd - authorityStart);
            if (authority.IndexOf('@') >= 0) return false;
            int port = authority.IndexOf(':');
            string host = port < 0 ? authority : authority.Substring(0, port);
            if (!(string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1")) return false;
            int matches = 0;
            foreach (string part in parsed.Query.TrimStart('?').Split('&'))
            {
                int separator = part.IndexOf('=');
                string name = separator < 0 ? part : part.Substring(0, separator);
                name = Uri.UnescapeDataString(name.Replace("+", " "));
                if (name != "lottieNativeStateTrace") continue;
                string value = separator < 0 ? "" : Uri.UnescapeDataString(part.Substring(separator + 1).Replace("+", " "));
                if (++matches != 1 || value != "1") return false;
            }
            return matches == 1;
        }

        private static void ValidateLayout(Type type, uint bytes)
        {
            if (Marshal.SizeOf(type) != bytes) throw new InvalidOperationException("WebGL native trace ABI size mismatch: " + type.Name);
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
                if (fields[i].FieldType != typeof(uint) || Marshal.OffsetOf(type, fields[i].Name).ToInt32() != i * 4)
                    throw new InvalidOperationException("WebGL native trace ABI offset mismatch: " + fields[i].Name);
        }

        private static uint Next(ref uint counter)
        {
            if (counter == uint.MaxValue) return 0;
            return ++counter;
        }

        private static uint UnityFrame()
        {
            int frame = Time.frameCount;
            if (frame >= 0) return (uint)frame;
            sFrameUnavailable = true;
            return 0;
        }

        internal static void TextureCreated(ref Identity identity)
        {
            if (!EnsureInitialized()) return;
            if (!identity.assigned)
            {
                identity.animationId = Next(ref sAnimationId);
                identity.assigned = true;
            }
            identity.origin = identity.generation == 0 ? 1u : 2u;
            if (Next(ref identity.generation) == 0) identity.generationUnavailable = true;
            identity.textureToken = Next(ref sTextureToken);
        }

        internal static int Register(ref Identity identity, IntPtr animation, IntPtr texture, int width, int height, bool recovery)
        {
            uint frame = UnityFrame();
            uint attempt = Next(ref identity.registrationAttempt);
            int result = NativeBridge.LottieWebGLTraceRegister(animation, texture, width, height,
                sFrameUnavailable ? 0 : identity.animationId, identity.generationUnavailable ? 0 : identity.generation, identity.textureToken,
                attempt, frame, recovery ? 3u : identity.origin);
            CheckStatus(result, "register", 1);
            return result;
        }

        internal static int Request(ref Identity identity, IntPtr animation, IntPtr renderData)
        {
            uint frame = UnityFrame();
            uint request = Next(ref identity.requestSequence);
            int result = NativeBridge.LottieWebGLTraceRequest(animation, renderData,
                sFrameUnavailable ? 0 : identity.animationId, identity.generationUnavailable ? 0 : identity.generation, request, frame);
            CheckStatus(result, "request", 1);
            return result;
        }

        internal static int IssueEvent()
        {
            uint frame = UnityFrame();
            // Token 1 is the unchanged rendering path when correlation cannot be represented.
            uint token = !sFrameUnavailable && sEventToken < int.MaxValue ? ++sEventToken : 1u;
            CheckStatus(NativeBridge.LottieWebGLTraceIssue(token, frame), "issue");
            return (int)token;
        }

        private static void CheckStatus(int result, string operation, int maximum = 0)
        {
            if (result < 0 || result > maximum) throw new InvalidOperationException("WebGL native trace " + operation + " protocol status " + result);
        }

        internal static string ExportJson()
        {
            // Explicit export never initializes/arms and never clears the native retained prefix.
            if (!sInitialized) return EmptyReport("initialization_missing");
            if (!sSelected) return EmptyReport("not_selected");
            IntPtr headerBuffer = IntPtr.Zero, recordBuffer = IntPtr.Zero;
            try
            {
                headerBuffer = Marshal.AllocHGlobal((int)HeaderBytes);
                recordBuffer = Marshal.AllocHGlobal((int)(RecordBytes * Capacity));
                int status = NativeBridge.LottieWebGLTraceCopy(Abi, HeaderBytes, RecordBytes,
                    headerBuffer, HeaderBytes, recordBuffer, Capacity, RecordBytes * Capacity);
                if (status != 0) return EmptyReport("protocol_failure", status, true);
                Header header = (Header)Marshal.PtrToStructure(headerBuffer, typeof(Header));
                if (header.magic != 0x4c575431 || header.abiVersion != Abi || header.headerBytes != HeaderBytes ||
                    header.recordBytes != RecordBytes || header.capacity != Capacity ||
                    header.storedRecords > Capacity || header.returnedRecords != header.storedRecords)
                    return EmptyReport("invalid_snapshot", 0, true);
                var json = new StringBuilder(1024 + (int)header.returnedRecords * 1200);
                json.Append("{\"schema\":\"lottie.webgl.native-state-trace.runtime/1\",\"enabled\":true,\"buildGuid\":");
                AppendString(json, sBuildGuid);
                json.Append(",\"managedFrameUnavailable\":").Append(sFrameUnavailable ? "true" : "false");
                json.Append(",\"interpretation\":\"Diagnostic getters precede the existing error drain. Zero checked upload error does not exonerate restoration, Unity Apply or other GL work. Coverage flags and exhaustion remain inconclusive for errors outside the retained prefix.\",\"header\":");
                AppendWords(json, header);
                json.Append(",\"records\":[");
                for (int i = 0; i < header.returnedRecords; i++)
                {
                    if (i != 0) json.Append(',');
                    Record record = (Record)Marshal.PtrToStructure(IntPtr.Add(recordBuffer, i * (int)RecordBytes), typeof(Record));
                    if (record.recordBytes != RecordBytes || record.abiVersion != Abi)
                        return EmptyReport("invalid_snapshot", 0, true);
                    AppendWords(json, record);
                }
                return json.Append("]}").ToString();
            }
            finally
            {
                if (recordBuffer != IntPtr.Zero) Marshal.FreeHGlobal(recordBuffer);
                if (headerBuffer != IntPtr.Zero) Marshal.FreeHGlobal(headerBuffer);
            }
        }

        private static string EmptyReport(string reason, int status = 0, bool enabled = false)
        {
            var json = new StringBuilder();
            json.Append("{\"schema\":\"lottie.webgl.native-state-trace.runtime/1\",\"enabled\":").Append(enabled ? "true" : "false");
            json.Append(",\"reason\":"); AppendString(json, reason);
            json.Append(",\"buildGuid\":"); AppendString(json, sBuildGuid ?? "");
            json.Append(",\"protocolStatus\":").Append(status.ToString(CultureInfo.InvariantCulture));
            return json.Append(",\"header\":null,\"records\":[]}").ToString();
        }

        private static void AppendWords<T>(StringBuilder json, T value) where T : struct
        {
            json.Append('{');
            FieldInfo[] fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                if (i != 0) json.Append(',');
                AppendString(json, fields[i].Name);
                json.Append(':').Append(((uint)fields[i].GetValue(value)).ToString(CultureInfo.InvariantCulture));
            }
            json.Append('}');
        }

        private static void AppendString(StringBuilder json, string value)
        {
            json.Append('"');
            foreach (char c in value ?? "")
            {
                if (c == '"' || c == '\\') json.Append('\\').Append(c);
                else if (c < 32) json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else json.Append(c);
            }
            json.Append('"');
        }
    }
}
#endif
