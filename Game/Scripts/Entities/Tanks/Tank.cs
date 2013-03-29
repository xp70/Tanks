﻿using System;
using System.Linq;
using CryEngine;
using CryEngine.Serialization;
using CryGameCode.Entities;

namespace CryGameCode.Tanks
{
	public partial class Tank : DamageableActor
	{
		public Tank()
		{
			Debug.LogAlways("[Enter] Tank.ctor: actor {0}", Id);
			//0 - right tread
			//1 - left tread
			m_treads[0] = new TankTread(this, new Vec2(1.5f, 0), 3000.0f);
			m_treads[1] = new TankTread(this, new Vec2(-1.5f, 0), 3000.0f);

			MaxHealth = 100;

			Input = new PlayerInput(this);
			Input.OnInputChanged += OnInputChanged;
			
			OnDeath += OnDied;
		}

		public void OnInputChanged(InputFlags flags, KeyEvent keyEvent)
		{
			if (flags.IsSet(InputFlags.LeftMouseButton) && keyEvent == KeyEvent.OnRelease)
			{
				var gameRules = GameRules.Current as SinglePlayer;

				if (IsDead)
				{
					// Set team &  type, sent to server and remote clients on revival. (TODO: Allow picking via UI)
					Team = gameRules.Teams.ElementAt(SinglePlayer.Selector.Next(0, gameRules.Teams.Length));

					if (string.IsNullOrEmpty(GameCVars.ForceTankType))
						TurretTypeName = GameCVars.TurretTypes[SinglePlayer.Selector.Next(GameCVars.TurretTypes.Count)].FullName;
					else
						TurretTypeName = "CryGameCode.Tanks." + GameCVars.ForceTankType;

					if (Game.IsServer)
						gameRules.RequestRevive(Id, Team, TurretTypeName);
					else
						RemoteInvocation(gameRules.RequestRevive, NetworkTarget.ToServer, Id, Team, TurretTypeName);
				}
				else if (IsDead)
					Debug.LogAlways("Can not request revive on living actor.");
			}
		}

		/// <summary>
		/// Called when the client has finished loading and is ready to play.
		/// </summary>
		public void OnEnteredGame()
		{
			if (IsLocalClient)
				Input.RegisterInputs();

			Debug.LogAlways("[Enter] Tank.OnEnteredGame: actor {0}", Id);

			PrePhysicsUpdateMode = PrePhysicsUpdateMode.Always;
			ReceivePostUpdates = true;

			Flags |= EntityFlags.CastShadow;
			ZoomLevel = 1;
			Health = 0;
			Hide(true);
			ReceiveUpdates = true;
		}

		public void OnLeftGame()
		{
			if (Input != null)
				Input.Destroy();

			if (Turret != null)
			{
				Turret.Destroy();
				Turret = null;
			}
			if (m_treads[0] != null)
				m_treads[0] = null;
			if (m_treads[1] != null)
				m_treads[1] = null;

		}

		protected override void NetSerialize(CrySerialize serialize, int aspect, byte profile, int flags)
		{
			serialize.BeginGroup("TankActor");

			// input aspect
			if (aspect == PlayerInput.Aspect)
			{
				if (Input != null)
					Input.NetSerialize(serialize);
				else
					serialize.FlagPartialRead();
			}

			if (aspect == MovementAspect)
			{
				if (Game.IsServer)
				{
					m_serverPos = Position;
					m_serverRot = Rotation;
				}

				serialize.Value("pos", ref m_serverPos, "wrld");
				serialize.Value("rot", ref m_serverRot);
			}

			serialize.EndGroup();
		}

		void ResetModel()
		{
			LoadObject("objects/tanks/tank_generic_" + Team + ".cdf");

			Physicalize();
		}

		public void OnRevived()
		{
			Turret = Activator.CreateInstance(Type.GetType(TurretTypeName), this) as TankTurret;

			Health = MaxHealth;

			ResetModel();

			Hide(false);

			if (IsLocalClient)
				Entity.Spawn<Cursor>("Cursor");

			SpawnTime = Time.FrameStartTime;
		}

		protected override void OnEditorReset(bool enteringGame)
		{
			Health = 0;

			if (enteringGame)
				ToggleSpectatorPoint();
		}

		void Physicalize()
		{
            var physicalizationParams = new PhysicalizationParams(PhysicalizationType.Living);

            physicalizationParams.mass = 500;
            physicalizationParams.slot = 0;
            physicalizationParams.flagsOR = PhysicalizationFlags.MonitorPostStep;

            physicalizationParams.livingDimensions.heightCollider = 2.5f;
            physicalizationParams.livingDimensions.sizeCollider = new Vec3(2.2f, 2.2f, 1.2f);
            physicalizationParams.livingDimensions.heightPivot = 0;

            physicalizationParams.livingDynamics.gravity = Vec3.Zero;
            physicalizationParams.livingDynamics.kAirControl = 0;

            Physicalize(physicalizationParams);
		}

		/*private void Reset(bool enteringGame)
		{
		    Physicalize();	

            m_acceleration = new Vec2();
		}*/

		public override void OnUpdate()
		{
			if (IsDead) 
				return;

			Turret.Update();

			if (Physics.Status != null)
			{
				float blend = MathHelpers.Clamp(Time.DeltaTime / 0.15f, 0, 1.0f);
				GroundNormal = (GroundNormal + blend * (Physics.Status.Living.GroundNormal - GroundNormal));
			}

			if (Game.IsPureClient)
			{
				var currentPos = Position;
				var currentRot = Rotation;

				m_currentDelta = m_serverPos - currentPos;
				var deltaLength = m_currentDelta.Length;

				if (IsLocalClient)
				{
					Renderer.DrawTextToScreen(10, 10, 2, Color.White, "Client pos: {0}", currentPos);
					Renderer.DrawTextToScreen(10, 30, 2, Color.White, "Server pos: {0}", m_serverPos);

					var clr = deltaLength > 2 ? Color.Red : Color.White;
					Renderer.DrawTextToScreen(10, 50, 2, clr, "Delta: {0}", deltaLength);
				}

				// Start forcing sync if we have to
				// TODO: Tweak based on connection
				if (m_currentDelta.Length > MaxDelta)
				{
					Position = m_serverPos;
				}

				Rotation = Quat.CreateNlerp(currentRot, m_serverRot, Time.DeltaTime * 20);
			}

			if (Turret != null && Turret.TurretEntity != null && !Turret.TurretEntity.IsDestroyed)
				Turret.TurretEntity.Position = Position + Rotation * new Vec3(0, 0.69252968f, 2.05108f);
		}

		protected override void OnPrePhysicsUpdate()
		{
			if (Input != null)
				Input.PreUpdate();

			UpdateMovement();
		}

		public void ToggleSpectatorPoint(bool increment = false)
		{
			if (!IsDead)
				return;

			var spectatorPoints = Entity.GetByClass<SpectatorPoint>();
			var spectatorPointCount = spectatorPoints.Count();

			if (spectatorPointCount > 0)
			{
				if (increment)
					CurrentSpectatorPoint++;

				if (CurrentSpectatorPoint >= spectatorPointCount)
					CurrentSpectatorPoint = 0;

				var iSpectatorPoint = SinglePlayer.Selector.Next(CurrentSpectatorPoint, spectatorPointCount);
				var spectatorPoint = spectatorPoints.ElementAt(iSpectatorPoint);

				Position = spectatorPoint.Position;
				Rotation = spectatorPoint.Rotation;
			}

			Hide(true);
		}

		string team;
		public string Team
		{
			get { return team ?? "red"; }
			set
			{
				var gameRules = GameRules.Current as SinglePlayer;
				if (gameRules.IsTeamValid(value))
				{
					team = value;

					// Load correct model for this team
					ResetModel();
				}
			}
		}

		private Vec3 m_currentDelta;
		private Vec3 m_serverPos;
		private Quat m_serverRot;

		private TankTread[] m_treads = new TankTread[2];

		public string TurretTypeName { get; set; }

		public PlayerInput Input { get; set; }

		public TankTurret Turret { get; set; }

		public Vec3 GroundNormal { get; set; }

		public int CurrentSpectatorPoint { get; set; }

		/// <summary>
		/// Time at which the player was last spawned.
		/// </summary>
		public float SpawnTime { get; set; }
	}
}
