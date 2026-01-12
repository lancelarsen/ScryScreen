using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ScryScreen.App.Services;

namespace ScryScreen.App.Controls;

public sealed class QuakeShakeDecorator : Decorator
{
	public static readonly StyledProperty<bool> QuakeEnabledProperty =
		AvaloniaProperty.Register<QuakeShakeDecorator, bool>(nameof(QuakeEnabled));

	public static readonly StyledProperty<double> QuakeIntensityProperty =
		AvaloniaProperty.Register<QuakeShakeDecorator, double>(nameof(QuakeIntensity), defaultValue: 0.5);

	public static readonly StyledProperty<long> QuakeTriggerProperty =
		AvaloniaProperty.Register<QuakeShakeDecorator, long>(nameof(QuakeTrigger), defaultValue: 0);

	public static readonly StyledProperty<bool> EmitAudioEventsProperty =
		AvaloniaProperty.Register<QuakeShakeDecorator, bool>(nameof(EmitAudioEvents), defaultValue: false);

	public static readonly StyledProperty<int> AudioPortalNumberProperty =
		AvaloniaProperty.Register<QuakeShakeDecorator, int>(nameof(AudioPortalNumber), defaultValue: 0);

	public bool QuakeEnabled { get => GetValue(QuakeEnabledProperty); set => SetValue(QuakeEnabledProperty, value); }
	public double QuakeIntensity { get => GetValue(QuakeIntensityProperty); set => SetValue(QuakeIntensityProperty, value); }
	public long QuakeTrigger { get => GetValue(QuakeTriggerProperty); set => SetValue(QuakeTriggerProperty, value); }

	public bool EmitAudioEvents { get => GetValue(EmitAudioEventsProperty); set => SetValue(EmitAudioEventsProperty, value); }
	public int AudioPortalNumber { get => GetValue(AudioPortalNumberProperty); set => SetValue(AudioPortalNumberProperty, value); }

	private readonly DispatcherTimer _timer;
	private readonly Random _rng = new();

	private readonly TranslateTransform _translate = new();
	private readonly RotateTransform _rotate = new();
	private readonly ScaleTransform _scale = new(1, 1);

	private DateTime _lastTickUtc = DateTime.UtcNow;

	private double _quakeTimeRemaining;
	private double _quakeDuration;
	private double _quakePhase;
	private double _quakeAmplitude;
	private double _sessionElapsed;

	private double _prevIntensity;
	private long _prevTrigger;
	private bool _wasQuaking;

	public QuakeShakeDecorator()
	{
		RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
		RenderTransform = new TransformGroup
		{
			Children = new Transforms { _scale, _rotate, _translate },
		};

		_timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) => Tick());
		this.AttachedToVisualTree += (_, _) => { _lastTickUtc = DateTime.UtcNow; _timer.Start(); };
		this.DetachedFromVisualTree += (_, _) => _timer.Stop();
	}

	private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

	private static double SmoothStep01(double t)
	{
		t = Clamp01(t);
		return t * t * (3 - (2 * t));
	}

	private static double ClampMin0(double v)
	{
		if (double.IsNaN(v) || double.IsInfinity(v))
		{
			return 0;
		}

		return v < 0 ? 0 : v;
	}

	private void ResetTransform()
	{
		_translate.X = 0;
		_translate.Y = 0;
		_rotate.Angle = 0;
	}

	private void Tick()
	{
		var now = DateTime.UtcNow;
		var dt = (now - _lastTickUtc).TotalSeconds;
		_lastTickUtc = now;

		if (dt <= 0)
		{
			return;
		}

		dt = Math.Min(dt, 0.10);

		var density = ClampMin0(QuakeIntensity);
		if (!QuakeEnabled)
		{
			if (_wasQuaking)
			{
				_wasQuaking = false;
				if (EmitAudioEvents)
				{
					EffectsEventBus.RaiseQuakeEnded(AudioPortalNumber);
				}
			}

			_prevIntensity = 0;
			_prevTrigger = 0;
			_quakeTimeRemaining = 0;
			_quakeDuration = 0;
			_sessionElapsed = 0;
			_scale.ScaleX = 1.0;
			_scale.ScaleY = 1.0;
			ResetTransform();
			return;
		}

		// As soon as the Quake effect is enabled, zoom-in slightly to avoid showing outside
		// edges while shaking (matches prior behavior).
		_scale.ScaleX = 1.10;
		_scale.ScaleY = 1.10;

		// Quake enabled but with zero intensity: keep the zoom, but do not shake.
		if (density <= 0)
		{
			if (_wasQuaking)
			{
				_wasQuaking = false;
				if (EmitAudioEvents)
				{
					EffectsEventBus.RaiseQuakeEnded(AudioPortalNumber);
				}
			}

			_prevIntensity = 0;
			_quakeTimeRemaining = 0;
			_quakeDuration = 0;
			_sessionElapsed = 0;
			ResetTransform();
			return;
		}

		// Use a single quake session per trigger, matching the quake audio length.
		const double SessionDurationSeconds = 10.0;
		const double RampUpSeconds = 2.0;
		const double RampDownSeconds = 0.75;

		var capped = Math.Min(density, 5.0);
		var ease = Clamp01((capped - 0.10) / 4.90);

		void StartQuakeSession()
		{
			_quakeDuration = SessionDurationSeconds;
			_quakeTimeRemaining = _quakeDuration;
			_sessionElapsed = 0;
			_quakePhase = _rng.NextDouble() * Math.PI * 2.0;

			// Base amplitude is scaled by intensity (0.1..5). Tune to taste.
			_quakeAmplitude = 2 + (18 * Math.Pow(capped, 0.70));

			if (!_wasQuaking)
			{
				_wasQuaking = true;
				if (EmitAudioEvents)
				{
					EffectsEventBus.RaiseQuakeStarted(AudioPortalNumber, capped);
				}
			}
		}

		// Manual trigger from UI (a nonce that increments).
		if (QuakeTrigger != _prevTrigger)
		{
			_prevTrigger = QuakeTrigger;
			// Prevent stacking/restarting: if a quake is already in progress, ignore the trigger.
			// We still consume the trigger value so it doesn't "queue" an immediate restart.
			if (_quakeTimeRemaining <= 0)
			{
				StartQuakeSession();
			}
		}

		// Progress the current quake session (if active).
		if (_quakeTimeRemaining > 0)
		{
			_quakeTimeRemaining = Math.Max(0, _quakeTimeRemaining - dt);
			_sessionElapsed = Math.Min(_quakeDuration, _sessionElapsed + dt);
		}

		if (_quakeTimeRemaining > 0)
		{
			var t = _quakeDuration <= 0 ? 0 : Clamp01(_sessionElapsed / _quakeDuration);

			// Match the audio: gradual build for the first ~2 seconds.
			var rampUp = SmoothStep01(_sessionElapsed / RampUpSeconds);
			// Gentle tail-off to avoid an abrupt stop when the sound ends.
			var rampDown = SmoothStep01(_quakeTimeRemaining / RampDownSeconds);
			var envelope = rampUp * rampDown;

			// Continuously update amplitude so slider/intensity changes take effect mid-quake.
			var amp = 2 + (18 * Math.Pow(capped, 0.70));
			var baseAmp = amp * envelope;

			// High-frequency shake with a tiny low-frequency sway.
			var x = Math.Sin((_quakePhase + (t * 70.0))) * baseAmp;
			x += Math.Sin((_quakePhase * 1.7) + (t * 110.0)) * baseAmp * 0.35;
			x += Math.Sin((_quakePhase * 0.15) + (t * 6.0)) * baseAmp * 0.12;

			var y = Math.Sin((_quakePhase * 0.8) + (t * 60.0)) * baseAmp * 0.35;
			y += Math.Sin((_quakePhase * 2.1) + (t * 95.0)) * baseAmp * 0.18;

			var rot = Math.Sin((_quakePhase * 0.4) + (t * 28.0)) * Math.Min(2.0, amp * 0.07) * envelope;

			_translate.X = x;
			_translate.Y = y;
			_rotate.Angle = rot;
		}
		else
		{
			if (_wasQuaking)
			{
				_wasQuaking = false;
				if (EmitAudioEvents)
				{
					EffectsEventBus.RaiseQuakeEnded(AudioPortalNumber);
				}
			}

			ResetTransform();
		}

		_prevIntensity = capped;
		InvalidateVisual();
	}

	private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}