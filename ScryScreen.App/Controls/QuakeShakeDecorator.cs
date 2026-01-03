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

	private double _timeToNextQuake;
	private double _quakeTimeRemaining;
	private double _quakeDuration;
	private double _quakePhase;
	private double _quakeAmplitude;

	private bool _prevEnabled;
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
		if (!QuakeEnabled || density <= 0)
		{
			if (_wasQuaking)
			{
				_wasQuaking = false;
				if (EmitAudioEvents)
				{
					EffectsEventBus.RaiseQuakeEnded(AudioPortalNumber);
				}
			}

			_prevEnabled = false;
			_prevIntensity = 0;
			_prevTrigger = 0;
			_timeToNextQuake = 0;
			_quakeTimeRemaining = 0;
			_scale.ScaleX = 1.0;
			_scale.ScaleY = 1.0;
			ResetTransform();
			return;
		}

		// Slight zoom-in to avoid showing outside edges while shaking.
		_scale.ScaleX = 1.10;
		_scale.ScaleY = 1.10;

		var capped = Math.Min(density, 5.0);
		var ease = Clamp01((capped - 0.10) / 4.90);

		double NextIntervalSeconds()
		{
			// Rare at low, frequent at high.
			var min = Lerp(60, 6, ease);
			var max = Lerp(85, 12, ease);
			return min + (_rng.NextDouble() * (max - min));
		}

		void StartQuake()
		{
			_quakeDuration = 0.55 + (_rng.NextDouble() * (0.95 + (0.35 * ease)));
			_quakeTimeRemaining = _quakeDuration;
			_quakePhase = _rng.NextDouble() * Math.PI * 2.0;

			// Max amplitude ~40px at intensity 5.
			_quakeAmplitude = 3 + (15 * Math.Pow(capped, 0.60));

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
			StartQuake();
			_timeToNextQuake = NextIntervalSeconds();
		}

		// Priming: if just enabled or intensity bumped up, show a quake fairly soon at higher values.
		if (!_prevEnabled || (capped > _prevIntensity + 0.6))
		{
			if (ease > 0.55)
			{
				_timeToNextQuake = Math.Min(_timeToNextQuake <= 0 ? double.PositiveInfinity : _timeToNextQuake, 0.8);
			}
		}

		// Schedule / progress.
		if (_quakeTimeRemaining > 0)
		{
			_quakeTimeRemaining -= dt;
		}
		else
		{
			_timeToNextQuake -= dt;
			if (_timeToNextQuake <= 0)
			{
				StartQuake();
				_timeToNextQuake = NextIntervalSeconds();
			}
		}

		if (_quakeTimeRemaining > 0)
		{
			var t = 1.0 - (_quakeTimeRemaining / _quakeDuration);
			var envelope = Math.Sin(Math.PI * t); // 0..1..0

			var baseAmp = _quakeAmplitude * envelope;

			var x = Math.Sin((_quakePhase + (t * 20.0))) * baseAmp;
			x += Math.Sin((_quakePhase * 1.7) + (t * 35.0)) * baseAmp * 0.25;

			var y = Math.Sin((_quakePhase * 0.8) + (t * 18.0)) * baseAmp * 0.08;

			var rot = Math.Sin((_quakePhase * 0.4) + (t * 12.0)) * Math.Min(2.0, _quakeAmplitude * 0.06) * envelope;

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

		_prevEnabled = true;
		_prevIntensity = capped;
		InvalidateVisual();
	}

	private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}