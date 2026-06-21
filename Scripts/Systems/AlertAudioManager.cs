using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Godot;

namespace ReactorCoreSim.Scripts.Systems
{
    public enum AlertSoundType
    {
        Warning,
        Critical,
        Scram,
        DnbrAlert,
        PowerPulse,
        RodMovement
    }

    public sealed class AlertAudioManager : IDisposable
    {
        private static readonly Lazy<AlertAudioManager> _instance = new(
            () => new AlertAudioManager(),
            LazyThreadSafetyMode.ExecutionAndPublication
        );

        public static AlertAudioManager Instance => _instance.Value;

        private readonly ConcurrentDictionary<AlertSoundType, AudioStream> _streamCache;
        private readonly ConcurrentBag<AudioStreamPlayer> _playerPool;
        private readonly HashSet<AudioStreamPlayer> _activePlayers;
        private readonly object _activeLock = new();
        private readonly object _initializationLock = new();

        private Node? _parentNode;
        private volatile bool _isInitialized;
        private volatile bool _isDisposed;
        private int _maxPoolSize = 32;

        private const float WarningVolume = 0.4f;
        private const float CriticalVolume = 0.7f;
        private const float ScramVolume = 0.9f;
        private const float DefaultPitch = 1.0f;

        private AlertAudioManager()
        {
            _streamCache = new ConcurrentDictionary<AlertSoundType, AudioStream>();
            _playerPool = new ConcurrentBag<AudioStreamPlayer>();
            _activePlayers = new HashSet<AudioStreamPlayer>(ReferenceEqualityComparer.Instance);
        }

        public void Initialize(Node parentNode)
        {
            if (_isInitialized) return;

            lock (_initializationLock)
            {
                if (_isInitialized) return;
                if (_isDisposed) throw new ObjectDisposedException(nameof(AlertAudioManager));

                _parentNode = parentNode ?? throw new ArgumentNullException(nameof(parentNode));

                EnsureStreamCachePopulated();
                PreWarmPool(8);

                _isInitialized = true;
            }
        }

        private void EnsureStreamCachePopulated()
        {
            foreach (AlertSoundType type in Enum.GetValues<AlertSoundType>())
            {
                if (!_streamCache.ContainsKey(type))
                {
                    _streamCache[type] = CreateSyntheticStream(type);
                }
            }
        }

        private static AudioStream CreateSyntheticStream(AlertSoundType type)
        {
            int sampleRate = 44100;
            float duration = type switch
            {
                AlertSoundType.Warning => 0.4f,
                AlertSoundType.Critical => 0.6f,
                AlertSoundType.Scram => 1.2f,
                AlertSoundType.DnbrAlert => 0.5f,
                AlertSoundType.PowerPulse => 0.2f,
                AlertSoundType.RodMovement => 0.1f,
                _ => 0.3f
            };

            int sampleCount = (int)(sampleRate * duration);
            var samples = new float[sampleCount];

            float freq1 = type switch
            {
                AlertSoundType.Warning => 880f,
                AlertSoundType.Critical => 1200f,
                AlertSoundType.Scram => 600f,
                AlertSoundType.DnbrAlert => 1000f,
                AlertSoundType.PowerPulse => 440f,
                AlertSoundType.RodMovement => 2000f,
                _ => 600f
            };

            float freq2 = freq1 * (type == AlertSoundType.Scram ? 0.6f : 1.5f);

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = MathF.Exp(-t * 4.0f);

                float freq = Mathf.Lerp(freq1, freq2, t / duration);
                float wave = Mathf.Sin(2.0f * Mathf.Pi * freq * t);

                if (type == AlertSoundType.Critical || type == AlertSoundType.DnbrAlert)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(2.0f * Mathf.Pi * 20.0f * t);
                    wave *= pulse;
                }

                if (type == AlertSoundType.Scram)
                {
                    float wobble = 1.0f + 0.5f * Mathf.Sin(2.0f * Mathf.Pi * 15.0f * t);
                    wave *= wobble;
                }

                samples[i] = wave * envelope * 0.8f;
            }

            var wavStream = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = sampleRate,
                Stereo = false,
                Data = FloatToPcm16(samples)
            };

            return wavStream;
        }

        private static byte[] FloatToPcm16(float[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short value = (short)(Math.Clamp(samples[i], -1.0f, 1.0f) * short.MaxValue);
                bytes[i * 2] = (byte)(value & 0xFF);
                bytes[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }
            return bytes;
        }

        private void PreWarmPool(int count)
        {
            if (_parentNode == null) return;

            for (int i = 0; i < count; i++)
            {
                var player = CreatePooledPlayer();
                if (player != null)
                {
                    _playerPool.Add(player);
                }
            }
        }

        private AudioStreamPlayer? CreatePooledPlayer()
        {
            if (_parentNode == null || _isDisposed) return null;

            var player = new AudioStreamPlayer
            {
                Bus = "Master",
                VolumeDb = 0f,
                PitchScale = DefaultPitch,
                StreamPaused = false
            };

            try
            {
                _parentNode.CallDeferred(Node.MethodName.AddChild, player);
            }
            catch
            {
                return null;
            }

            player.Finished += () => OnPlayerFinished(player);

            return player;
        }

        public void PlayAlert(AlertSoundType type, float volumeDb = 0f, float pitch = 1.0f)
        {
            if (_isDisposed || !_isInitialized || _parentNode == null) return;

            AudioStreamPlayer? player = null;
            try
            {
                if (!_playerPool.TryTake(out player))
                {
                    lock (_activeLock)
                    {
                        if (_activePlayers.Count >= _maxPoolSize)
                        {
                            return;
                        }
                    }
                    player = CreatePooledPlayer();
                }

                if (player == null) return;

                if (!_streamCache.TryGetValue(type, out var stream) || stream == null)
                {
                    ReturnPlayerToPool(player);
                    return;
                }

                player.Stream = stream;
                player.VolumeDb = volumeDb + GetBaseVolume(type);
                player.PitchScale = Math.Clamp(pitch, 0.25f, 4.0f);
                player.StreamPaused = false;

                lock (_activeLock)
                {
                    if (!_activePlayers.Add(player))
                    {
                        ReturnPlayerToPool(player);
                        return;
                    }
                }

                PlaySafe(player);
            }
            catch
            {
                if (player != null)
                {
                    ReturnPlayerToPool(player);
                }
            }
        }

        private static float GetBaseVolume(AlertSoundType type)
        {
            return type switch
            {
                AlertSoundType.Warning => Mathf.LinearToDb(WarningVolume),
                AlertSoundType.Critical => Mathf.LinearToDb(CriticalVolume),
                AlertSoundType.Scram => Mathf.LinearToDb(ScramVolume),
                AlertSoundType.DnbrAlert => Mathf.LinearToDb(CriticalVolume * 0.8f),
                AlertSoundType.PowerPulse => Mathf.LinearToDb(WarningVolume * 0.7f),
                AlertSoundType.RodMovement => Mathf.LinearToDb(0.3f),
                _ => 0f
            };
        }

        private static void PlaySafe(AudioStreamPlayer player)
        {
            if (player.IsInsideTree())
            {
                try
                {
                    player.Play();
                }
                catch
                {
                }
            }
        }

        private void OnPlayerFinished(AudioStreamPlayer player)
        {
            if (_isDisposed)
            {
                SafeRemoveAndFree(player);
                return;
            }

            lock (_activeLock)
            {
                _activePlayers.Remove(player);
            }

            ReturnPlayerToPool(player);
        }

        private void ReturnPlayerToPool(AudioStreamPlayer player)
        {
            if (player == null) return;
            if (_isDisposed)
            {
                SafeRemoveAndFree(player);
                return;
            }

            try
            {
                if (player.IsPlaying())
                {
                    player.Stop();
                }

                player.StreamPaused = false;
                player.Stream = null;
                player.VolumeDb = 0f;
                player.PitchScale = DefaultPitch;

                lock (_activeLock)
                {
                    _activePlayers.Remove(player);
                }

                int currentPoolSize = _playerPool.Count;
                if (currentPoolSize < _maxPoolSize)
                {
                    _playerPool.Add(player);
                }
                else
                {
                    SafeRemoveAndFree(player);
                }
            }
            catch
            {
                SafeRemoveAndFree(player);
            }
        }

        private static void SafeRemoveAndFree(AudioStreamPlayer player)
        {
            if (player == null) return;

            try
            {
                if (player.IsPlaying())
                {
                    player.Stop();
                }
            }
            catch
            {
            }

            try
            {
                if (IsInstanceValid(player) && player.GetParent() != null)
                {
                    player.GetParent().CallDeferred(Node.MethodName.RemoveChild, player);
                }
                if (IsInstanceValid(player))
                {
                    player.QueueFree();
                }
            }
            catch
            {
            }
        }

        public void CleanupStalePlayers()
        {
            if (_isDisposed) return;

            lock (_activeLock)
            {
                var toRemove = new List<AudioStreamPlayer>();
                foreach (var player in _activePlayers)
                {
                    bool isStale = false;
                    try
                    {
                        if (!IsInstanceValid(player) ||
                            !player.IsInsideTree() ||
                            (player.GetParent() == null) ||
                            !player.IsPlaying())
                        {
                            isStale = true;
                        }
                    }
                    catch
                    {
                        isStale = true;
                    }

                    if (isStale)
                    {
                        toRemove.Add(player);
                    }
                }

                foreach (var player in toRemove)
                {
                    _activePlayers.Remove(player);
                    SafeRemoveAndFree(player);
                }
            }
        }

        public void StopAllAlerts()
        {
            if (_isDisposed) return;

            lock (_activeLock)
            {
                var copy = new List<AudioStreamPlayer>(_activePlayers);
                foreach (var player in copy)
                {
                    try
                    {
                        if (player.IsPlaying())
                        {
                            player.Stop();
                        }
                    }
                    catch
                    {
                    }
                    ReturnPlayerToPool(player);
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            _isInitialized = false;

            StopAllAlerts();

            lock (_activeLock)
            {
                foreach (var player in _activePlayers)
                {
                    SafeRemoveAndFree(player);
                }
                _activePlayers.Clear();
            }

            while (_playerPool.TryTake(out var player))
            {
                SafeRemoveAndFree(player);
            }

            _streamCache.Clear();
            _parentNode = null;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<AudioStreamPlayer>
        {
            public static readonly ReferenceEqualityComparer Instance = new();
            public bool Equals(AudioStreamPlayer? x, AudioStreamPlayer? y) => ReferenceEquals(x, y);
            public int GetHashCode(AudioStreamPlayer? obj) => obj?.GetHashCode() ?? 0;
        }
    }
}
