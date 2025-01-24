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
	[Draw("sv_accelerate")] public float sv_accelerate = 10f;
	[Draw("run speed")] public float runSpeed = 10f;
	[Draw("max_wishspd")] public float maxWishspd = 0.1f;
	[Draw("ground accel")] public float ground_accelerate = 0.1f;
	[Draw("movement speed multiplier")] public float speedmult = 1f;
	[Draw("ABH (portal/hl2)")] public bool abh = false;
	[Draw("Max ABH spd")] public float maxAbhSpeed = 1000f;

	public override void Save(UnityModManager.ModEntry modEntry)
	{
		Save(this, modEntry);
	}

	public void OnChange() { }
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
		if (!CustomFirstPersonController_Patch.previouslyGrounded)
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

	[HarmonyPatch("CanMove", MethodType.Getter), HarmonyPostfix]
	public static void CanMove_Postfix(ref bool __result)
	{
		canMove = __result;
		__result = false;
	}

	[HarmonyPatch("CharacterMovement"), HarmonyPostfix]
	public static void CharacterMovement_Postfix(CustomFirstPersonController __instance, Vector3 ___desiredMove, Vector3 ___m_MoveDir)
	{
		if (!__instance.capsule.enabled)
		{
			velocity = Vector3.zero;
			return;
		}

		velocity = __instance.capsule.velocity;
		horizontalVelocity = velocity.xz();

		if (__instance.m_Jumping || !previouslyGrounded)
		{
			if (previouslyGrounded && !__instance.IsCrouching && !previouslypreviouslyGrounded)
			{
				__instance.SetCapsuleHeight(1.1f);
			}

			float runSpeed = Main.settings.runSpeed;

			if (__instance.IsCrouching)
			{
				runSpeed *= 0.75f;
			}

			Vector2 wishVeloc = ___desiredMove.xz().normalized * runSpeed;

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
		else
		{
			velocity = ___m_MoveDir;
		}

		previouslypreviouslyGrounded = previouslyGrounded;
		previouslyGrounded = __instance.previouslyGrounded;
		lastCapsuleHeight = __instance.CapsuleHeight;

		if (canMove)
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
		{
			___crouchJumped = true;
		}
	}
}
