using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

using UnityEngine;
using DV;
using static UnityModManagerNet.UnityModManager;
using dnlib;
using System.Collections.Generic;
using VLB;

namespace movement_mod;

#if DEBUG
[EnableReloading]
#endif
public static class Main
{
	private static Harmony? harmony;
	public static ModEntry mod = null!;
	public static Settings settings = new();
	public static ModEntry.ModLogger logger
	{
		get
		{
			return mod.Logger;
		}
	}

	public static bool Load(UnityModManager.ModEntry modEntry)
	{
		try
		{
			mod = modEntry;
			modEntry.OnUnload = Unload;
			settings = Settings.Load<Settings>(modEntry);

			modEntry.OnGUI = OnGUI;
			modEntry.OnSaveGUI = OnSaveGUI;

			harmony = new Harmony(modEntry.Info.Id);
			harmony.PatchAll(Assembly.GetExecutingAssembly());

		}
		catch (Exception ex)
		{
			modEntry.Logger.LogException($"Failed to load {modEntry.Info.DisplayName}:", ex);
			harmony?.UnpatchAll(modEntry.Info.Id);
			return false;
		}

		return true;
	}

	public static bool Unload(UnityModManager.ModEntry modEntry)
	{
		harmony?.UnpatchAll(mod?.Info.Id);

		return true;
	}
	static void OnGUI(UnityModManager.ModEntry modEntry)
	{
		settings?.Draw(modEntry);
	}

	static void OnSaveGUI(UnityModManager.ModEntry modEntry)
	{
		settings?.Save(modEntry);
	}

}

public class Settings : ModSettings, IDrawable
{
	[Draw("Auto Bunnyhopping")] public bool autoBhop = false;
	[Draw("sv_airaccelerate")] public float sv_accelerate = 5f;
	[Draw("run speed")] public float runSpeed = 6f;
	[Draw("max_wishspd")] public float maxWishspd = 1f;
	[Draw("movement speed multiplier")] public float speedmult = 1f;
	[Draw("ABH (portal/hl2)")] public bool abh = false;
	[Draw("Max ABH spd")] public float maxAbhSpeed = 250f;
	[Draw("Mod ground movement")] public bool walk = true;
	[Draw("Ground accelerate")] public float ground_accelerate = 10f;
	[Draw("Ground decelerate")] public float ground_decelerate = 5f;
	[Draw("Static accel factor")] public float static_accel = 1f;

	[Space]
	[Draw("Reset")] public bool reset = false;

	public override void Save(UnityModManager.ModEntry modEntry)
	{
		Save(this, modEntry);
	}

	public void OnChange()
	{
		if (reset)
		{
			autoBhop = false;
			sv_accelerate = 5f;
			runSpeed = 6f;
			maxWishspd = 1f;
			speedmult = 1f;
			abh = false;
			maxAbhSpeed = 250f;
			walk = true;
			ground_accelerate = 10f;
			ground_decelerate = 5f;
			static_accel = 1f;

			reset = false;
		}
	}
}

public static class SvUser
{
	public static void SV_AirAccelerate(Vector2 wishveloc, ref Vector2 velocity)
	{
		float addspeed, wishspeed, wishspd, accelspeed, currentspeed;

		wishspd = wishveloc.magnitude;
		wishveloc.Normalize();
		wishspeed = wishspd;
		if (wishspd > Main.settings.maxWishspd)
			wishspd = Main.settings.maxWishspd;
		currentspeed = Vector2.Dot(velocity, wishveloc);
		addspeed = wishspd - currentspeed;
		if (addspeed <= 0)
			return;
		accelspeed = Main.settings.sv_accelerate * wishspeed * Time.deltaTime;
		if (accelspeed > addspeed)
			accelspeed = addspeed;

		velocity += accelspeed * wishveloc;
	}
}


[HarmonyPatch(typeof(LocomotionInputNonVr))]
static class LocomotionInputNonVr_Patch
{
	[HarmonyPatch(nameof(LocomotionInputNonVr.JumpRequested), MethodType.Getter), HarmonyPostfix]
	public static void JumpRequested_Postfix(ref bool __result)
	{
		if (Main.settings.autoBhop)
			__result = KeyBindings.jumpKeys.IsPressed(false);
	}

	[HarmonyPatch("AxisSmoothing"), HarmonyPostfix]
	public static void AxisSmoothing_Postfix(float targetSpeed, ref float __result)
	{
		if (!CustomFirstPersonController_Patch.previouslyGrounded || Main.settings.walk)
			__result = targetSpeed;
	}
}

[HarmonyPatch(typeof(CustomFirstPersonController))]
static class CustomFirstPersonController_Patch
{
	public static Vector2 horizontalVelocity = Vector2.zero;
	public static Vector3 velocity = Vector3.zero;
	public static bool previouslyGrounded = true;
	public static bool previouslypreviouslyGrounded = true;
	public static float lastCapsuleHeight = -1f;
	public static bool canMove = false;
	public static bool reparented = false;

	[HarmonyPatch("CanMove", MethodType.Getter), HarmonyPostfix]
	public static void CanMove_Postfix(ref bool __result)
	{
		canMove = __result;
		__result = false;
	}

	[HarmonyPatch("CharacterMovement"), HarmonyPostfix]
	public static void CharacterMovement_Postfix(CustomFirstPersonController __instance, Vector3 ___desiredMove, Vector3 ___m_MoveDir)
	{
		if (__instance.isRepositioning)
		{
			velocity = Vector3.zero;
			return;
		}

		if (!__instance.capsule.enabled)
		{
			__instance.capsule.enabled = true;
			___m_MoveDir = Vector3.zero;
		}
		else if (!reparented)
		{
			velocity = __instance.capsule.velocity;
		}

		if (reparented) reparented = false;

		horizontalVelocity = velocity.xz();

		float runSpeed = Main.settings.runSpeed;

		if (__instance.IsCrouching)
			runSpeed *= 0.5f;

		if (__instance.m_Jumping || !previouslyGrounded)
		{
			if (previouslyGrounded && !__instance.IsCrouching && !previouslypreviouslyGrounded)
			{
				__instance.SetCapsuleHeight(1.1f);
			}

			Vector2 wishVeloc = ___m_MoveDir.xz();

			SvUser.SV_AirAccelerate(wishVeloc, ref horizontalVelocity);

			if (Main.settings.abh && previouslyGrounded)
			{
				if (horizontalVelocity.magnitude > runSpeed)
				{
					float diff = horizontalVelocity.magnitude - runSpeed;
					float sign = __instance.m_Input.y < -0.1f ? 1f : -1f;
					horizontalVelocity = Vector3.ClampMagnitude(horizontalVelocity + __instance.directionDevice.forward.xz().normalized * diff * sign, Main.settings.maxAbhSpeed);
				}
			}

			velocity.x = horizontalVelocity.x;
			velocity.z = horizontalVelocity.y;
			velocity.y = ___m_MoveDir.y;
		}
		else if (!__instance.underwater && Main.settings.walk)
		{
			Vector2 wishVeloc = ___m_MoveDir.xz();

			float accel = (wishVeloc.magnitude < 0.1f || (Vector2.Dot(wishVeloc.normalized, velocity.normalized) > 0f && wishVeloc.magnitude < velocity.magnitude))
				? accel = Main.settings.ground_decelerate
				: accel = Main.settings.ground_accelerate;

			horizontalVelocity = Vector2.Lerp(horizontalVelocity, wishVeloc, Mathf.Clamp01(accel * Time.deltaTime));
			horizontalVelocity = Vector2.MoveTowards(horizontalVelocity, wishVeloc, accel * Time.deltaTime * Main.settings.static_accel);

			velocity.x = horizontalVelocity.x;
			velocity.z = horizontalVelocity.y;
			velocity.y = ___m_MoveDir.y;
		}
		else
		{
			velocity = ___m_MoveDir;
		}

		previouslypreviouslyGrounded = previouslyGrounded;
		previouslyGrounded = __instance.previouslyGrounded;
		lastCapsuleHeight = __instance.CapsuleHeight;

		if (!__instance.isRepositioning)
		{
			__instance.capsule.Move(velocity * Time.deltaTime);
		}
	}

	[HarmonyPatch(nameof(CustomFirstPersonController.UpdateLocomotionValues)), HarmonyPostfix]
	public static void UpdateLocomotionValues_Postfix(CustomFirstPersonController __instance, float walkMult, float runMult)
	{
		float mult = Main.settings.speedmult;

		__instance.movementSpeedMultipiler *= mult;
		__instance.runSpeedMultipiler *= mult;
	}

	[HarmonyPatch("Update"), HarmonyPrefix]
	public static void Update_Prefix(CustomFirstPersonController __instance, ref bool ___crouchJumped)
	{
		if (__instance.IsCrouching && !___crouchJumped && !__instance.capsule.isGrounded)
			___crouchJumped = true;
	}
}

[HarmonyPatch(typeof(CharacterReparenting))]
public static class CharacterReparenting_Patch
{
	[HarmonyPatch(nameof(CharacterReparenting.ReparentTo)), HarmonyPostfix]
	public static void ReparentTo_Postfix(CharacterReparenting __instance, CharacterController ___charController, Transform target, bool forceReparent = false)
	{
		try
		{

			if (!forceReparent && target == __instance.transform.parent)
				return;

			bool flag1 = ___charController.transform.parent.TryGetComponent<Rigidbody>(out Rigidbody rb1);
			bool flag2 = target.TryGetComponent<Rigidbody>(out Rigidbody rb2);

			Vector3 vel1 = flag1 ? rb1.velocity : Vector3.zero;
			Vector3 vel2 = flag2 ? rb2.velocity : Vector3.zero;

			CustomFirstPersonController_Patch.velocity += vel2 - vel1;
			CustomFirstPersonController_Patch.reparented = true;
		}
		catch (Exception e)
		{
			Main.logger.LogException(e);
		}
	}
}
