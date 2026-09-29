using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace CombatPrep.Net
{
    /// <summary>
    /// Thin wrapper over Unity Multiplayer Services: anonymous sign-in, host a 4-player
    /// session with a join code, join by code, leave.
    ///
    /// Sessions start Netcode themselves - creating a session calls StartHost on
    /// NetworkManager.Singleton, joining calls StartClient - so nothing here touches the
    /// NetworkManager directly. Relay means nobody has to open ports on their router.
    /// </summary>
    public static class SessionService
    {
        public const int MaxPlayers = 4;

        public static ISession Current { get; private set; }
        public static string Code => Current?.Code;
        public static bool InSession => Current != null;

        /// <summary>Raised when the session goes away underneath us (host left, removed).</summary>
        public static event Action SessionEnded;

        public static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (AuthenticationService.Instance.IsSignedIn) return;

            // A fresh anonymous profile per launch. Multiplayer Play Mode clones, and a
            // build running beside the editor, share PlayerPrefs on Windows - without this
            // they would all sign in as the same player and collide in the lobby.
            AuthenticationService.Instance.SwitchProfile(
                "p" + Guid.NewGuid().ToString("N").Substring(0, 12));
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        /// <summary>Creates a private, code-joinable session and starts this machine as host.</summary>
        public static async Task<ISession> HostAsync()
        {
            await EnsureSignedInAsync();

            // Private: not listed publicly, joinable only with the code.
            var options = new SessionOptions { MaxPlayers = MaxPlayers, IsPrivate = true }
                .WithRelayNetwork();

            Current = await MultiplayerService.Instance.CreateSessionAsync(options);
            Hook(Current);
            return Current;
        }

        /// <summary>Joins an existing session by its code and starts this machine as a client.</summary>
        public static async Task<ISession> JoinAsync(string code)
        {
            await EnsureSignedInAsync();

            Current = await MultiplayerService.Instance.JoinSessionByCodeAsync(
                code.Trim().ToUpperInvariant());
            Hook(Current);
            return Current;
        }

        public static async Task LeaveAsync()
        {
            var session = Current;
            Current = null;
            if (session == null) return;

            Unhook(session);
            try
            {
                await session.LeaveAsync();
            }
            catch (Exception e)
            {
                // Leaving is best-effort: the session may already be gone (host quit).
                Debug.LogWarning($"[Session] Leave failed, continuing: {e.Message}");
            }
        }

        static void Hook(ISession s)
        {
            s.RemovedFromSession += OnSessionGone;
            s.Deleted += OnSessionGone;
        }

        static void Unhook(ISession s)
        {
            s.RemovedFromSession -= OnSessionGone;
            s.Deleted -= OnSessionGone;
        }

        static void OnSessionGone()
        {
            var s = Current;
            Current = null;
            if (s != null) Unhook(s);
            SessionEnded?.Invoke();
        }
    }
}
