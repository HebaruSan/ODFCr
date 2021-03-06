using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;
using UnityEngine.UI;

namespace ODFC
{
    /// <summary>
    /// On Demand Fuel Cells (ODFC) part module
    /// </summary>
    /// <seealso cref="PartModule" />
    public class ODFC : PartModule
    {
    #region Enums Vars

        public enum states : byte { error, off, nominal, fuelDeprived, noDemand, stalled }; // deploy, retract,
        private static readonly string[] STATES_STR = { "ERROR!", "Off", "Nominal", "Fuel Deprived", "No Demand", "Stalled" };
        private static readonly string[] STATES_COLOUR = { "<color=orange>", "<color=black>", "<#ADFF2F>", "<color=yellow>", "<#6495ED>", "<color=red>" };

        //                                                                                                  (Green)                          (Blue)
        private const string FuelTransferFormat = "0.##"; //FuelTransferFormat?

        private const float
            thresHoldSteps = 0.05f, // increment the rate by this amount (default is 5)
            thresholdMin = thresHoldSteps,
            thresHoldMax = 1;

        /// <summary>current counter for responseTime</summary>
        //* this should be highlighted
        //TODO: implement into autoHunt
        // ! important
        // ? backwards 
        // x test crossed
        //@param timeOut 
        // * highlighted
        internal int timeOut = 1;

        /// <summary>[internal]The fuel mode current ElectricCharge (EC) production. seealso OnDemandFuelCellsEC</summary>
        internal double _fuelModeMaxECRateLimit = 0f;
        public Double fuelModeMaxECRateLimit = 0f;

        /// <summary>
        /// Gets the On Demand Fuelcells(ODFC) Electric Charge (EC) Production.
        /// AMPYear / JPRepo / Background?
        /// allows AMPYear and others to see current EC/s production
        /// </summary>
        /// <value>
        /// The On Demand Fuelcells(ODFC) Electric Charge (EC) Production.
        /// </value>
        public double OnDemandFuelCellsEC { get { return this._fuelModeMaxECRateLimit; } }

        private double
            lastGen = -1,
            lastMax = -1,
            lastTF = -1;

        private int lastFuelMode = -1;
        /// <summary>Module information shown in editors</summary>
        private string info = string.Empty;

        /// <summary>The configuration node</summary>
        public ConfigNode configNode;

        /// <summary>The internalized ConfigNode String</summary>
        public string ConfigNodeString;

        /// <summary>List: The last resource</summary>
        public static List<ResourceLabel> lastResource = new List<ResourceLabel>();

        /// <summary>ElectricCharge identification number</summary>
        public static int ElectricChargeID;

        /// <summary>The ns</summary>
        public bool ns = true;

        /// <summary>The ODFC configuration</summary>
        public Config ODFC_config;

        /// <summary>The state of the Fuel Cell (nominal, off et al)</summary>
        public states state = states.error;

#endregion Enums Vars

        //x added PAW grouping, set to autocollapse - introduced in KSP 1.7.1
        //TODO: would really like the PAW to remember if the group was open

    #region Fields Events Actions

        //TODO: This is to be used to scale ODFC in the part.cfg. also might allow independant scaling of the module from the part as a whole.
        /// <summary>Not Implemented Yet. The internal part.cfg scalingFactor.</summary>
        [KSPField(isPersistant = true, 
            guiActive = false, 
            guiActiveEditor = false,
            guiName = "scaleFactor")]
        public float scalingFactor = 0f; // allows for scaling of ODFC elements

        [KSPField(isPersistant = false,
            guiActive = true,
            guiActiveEditor = true,
            guiName = " ")]
        public string PAWStatus = "ODFC: booting up. FCOS 0.42.17.. ";

        /// <summary>The fuel mode</summary>
        [KSPField(isPersistant = true,
            guiActive = false,
            guiActiveEditor = false,
            groupName = "ODFC")]
        public int fuelMode = 0;

        [KSPField(isPersistant = false,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Status",
            groupDisplayName = "On Demand Fuel Cells Control",
            groupStartCollapsed = true,
            groupName = "ODFC")]
        public string status = "ERROR!";

        [KSPField(isPersistant = false,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "EC/s (cur/max)",
            groupName = "ODFC")]
        public string ECs_status = "ERROR!";

        [KSPField(isPersistant = false,
            guiActive = false,
            guiActiveEditor = true,
            guiName = "Max EC/s",
            groupName = "ODFC")]
        public string maxECs_status = "ERROR!";

        [KSPField(isPersistant = false,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Fuel Used",
            groupName = "ODFC")]
        public string fuel_consumption = "ERROR!";

        [KSPField(isPersistant = false,
            guiActive = false,
            guiActiveEditor = false,
            guiName = "Byproducts",
            groupName = "ODFC")]
        public string byproducts = "ERROR!";

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Enabled:",
            groupName = "ODFC"),
            UI_Toggle(disabledText = "No",
            enabledText = "Yes")]
        public bool fuelCellIsEnabled = true;

        // changed from false to true 
        [KSPEvent(guiActive = true,
            guiActiveEditor = true,
            guiName = "Next Fuel Mode",
            groupName = "ODFC")]
        public void nextFuelMode()
        {
            if (++fuelMode >= ODFC_config.modes.Length)
                fuelMode = 0;

            updateEditor(); // updateFuelTexts();
        }

        [KSPEvent(guiActive = true,
            guiActiveEditor = true,
            guiName = "Previous Fuel Mode",
            groupName = "ODFC")]
        public void previousFuelMode()
        {
            if (--fuelMode < 0)
                fuelMode = ODFC_config.modes.Length - 1;

            updateEditor(); // updateFuelTexts();
        }

        /*
        TODO: future: convert rateLimit and threshold to use
        KSP 1.7.1 Added a new type for PAW fields, a double slider to set ranges with a min and max values
        UI_MinMaxRange
        */

        /// <summary>The rate limit
        /// (max % production)</summary>
        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Rate Limit:",
            guiFormat = "P0",
            groupName = "ODFC"),
            UI_FloatRange(minValue = thresholdMin, maxValue = thresHoldMax, stepIncrement = thresHoldSteps)]
        public float rateLimit = 1f;

        /// <summary>The current threshold (%) which needs to be equal or below before production begins.</summary>
        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "Threshold:",
            guiFormat = "P0",
            groupName = "ODFC"),
            UI_FloatRange(minValue = thresholdMin, maxValue = thresHoldMax, stepIncrement = thresHoldSteps)]
        public float threshold = 0.85f; //x thresholdMin;

        [KSPAction("Toggle Fuel Cell")]
        public void toggleAction(KSPActionParam kap)
        { fuelCellIsEnabled = !fuelCellIsEnabled; }

        [KSPAction("Enable Fuel Cell")]
        public void enableAction(KSPActionParam kap)
        { fuelCellIsEnabled = true; }

        [KSPAction("Disable Fuel Cell")]
        public void disableAction(KSPActionParam kap)
        { fuelCellIsEnabled = false; }

        [KSPAction("Next Fuel Mode")]
        public void nextFuelmodeAction(KSPActionParam kap)
        { nextFuelMode(); }

        [KSPAction("Previous Fuel Mode")]
        public void previousFuelModeAction(KSPActionParam kap)
        { previousFuelMode(); }

        [KSPAction("Decrease Rate Limit")]
        public void decreaseRateLimitAction(KSPActionParam kap)
        { rateLimit = Math.Max(rateLimit - thresHoldSteps, thresholdMin); }

        [KSPAction("Increase Rate Limit")]
        public void increaseRateLimitAction(KSPActionParam kap)
        { rateLimit = Math.Min(rateLimit + thresHoldSteps, thresHoldMax); }

        [KSPAction("Decrease Threshold")]
        public void decreaseThresholdAction(KSPActionParam kap)
        { threshold = Math.Max(threshold - thresHoldSteps, thresholdMin); }

        [KSPAction("Increase Threshold")]
        public void increaseThresholdAction(KSPActionParam kap)
        { threshold = Math.Min(threshold + thresHoldSteps, thresHoldMax); }
#endregion Fields Events Actions

        #region Private Functions

        /// <summary>
        /// Updates the fuel string.
        /// </summary>
        /// <param name="s">The s.</param>
        /// <param name="fuels">The fuels.</param>
        private void updateFuelString(out string s, Fuel[] fuels)
        {
            string fuelColorStr = "";
            string fuelRateColorStr = "";
            string endStr = "";

            // would like to have and if s = fuel_consumption then else byproducts then 
            // ie different colors for fuel_consumption and byproducts
            if (HighLogic.CurrentGame.Parameters.CustomParams<Options>().coloredPAW)
            {
                fuelColorStr = "<#FFFF00>";
                fuelRateColorStr = "<#FFFF00>";
                endStr = "</color>";
            }

            if (fuels.Length == 0)
            {
                s = "None";
                return;
            }

            s = "";
            //x bool plus = false;

            foreach (Fuel fuel in fuels)
            {
                ResourceLabel abr = lastResource.Find(x => x.resourceID == fuel.resourceID);
                /*x                if (plus)
                                    s += " + ";
                                // add code to verify found exists to prevent nullref
                                plus = true;*/
                //? THIS IS WHERE have to add code to include consumption/production #
                s += "\n" + fuelColorStr + PartResourceLibrary.Instance.GetDefinition(fuel.resourceID).name + ": " + fuelRateColorStr + RateString(fuel.rate) + endStr;
                // add code to verify found exists to prevent nullref 
            }
                Log.dbg("[ODFC PAW] Fuels {0}: " + s);
            //s += "\n";
        }
        private static string RateString(double Rate)
        {
            //  double rate = double.Parse(value.value);
            string sfx = "/s";
            if (Rate <= 0.004444444f)
            {
                Rate *= 3600;
                sfx = "/h";
            }
            else if (Rate < 0.2666667f)
            {
                Rate *= 60;
                sfx = "/m";
            }
            // limit decimal places to 10 and add sfx
            //return String.Format(FuelRateFormat, Rate, sfx);
            return Rate.ToString() + sfx;
        }
  
        /// <summary>Updates the fuel texts.</summary>
        private void updateFuelTexts()
            {
                updateFuelString(out fuel_consumption, ODFC_config.modes[fuelMode].fuels);
                updateFuelString(out byproducts, ODFC_config.modes[fuelMode].byproducts);
            }

        private void UpdateState(states newstate, Double gen, Double max)
        {
            if (gen != lastGen || max != lastMax)
            {
                lastGen = gen;
                lastMax = max;

                ECs_status = gen.ToString(FuelTransferFormat) + " / " + max.ToString(FuelTransferFormat);
            }

            if (state != newstate)
            {
                state = newstate;
                status = STATES_STR[(int)state];
            }

            double tf = gen / max * rateLimit;
            if (tf != lastTF || fuelMode != lastFuelMode)
            {
                lastTF = tf;
                lastFuelMode = fuelMode;
            }
            updatePAWLabel();
        }

        private string GetResourceRates(ConfigNode node)
        {
            if (node == null || node.values.Count < 1)
                return "\n - None";

            string resourceRates = "";

            foreach (ConfigNode.Value value in node.values)
            {
                double rate = double.Parse(value.value);
                string sfx = "/s";

                if (rate <= 0.004444444f)
                {
                    rate *= 3600;
                    sfx = "/h";
                }
                else if (rate < 0.2666667f)
                {
                    rate *= 60;
                    sfx = "/m";
                }

                resourceRates += "\n - " + value.name + ": " + rate.ToString() + sfx;
            }

            return resourceRates;
        }

        private void kommit(Fuel[] fuels, Double adjr)
        {
            foreach (Fuel fuel in fuels)
                part.RequestResource(fuel.resourceID, fuel.rate * adjr);
        }

        #endregion Private Functions

        #region internal Public Functions

        internal void updateConfig() // private
        {
            Log.dbg("Updating config");

            ODFC_config = new Config(ConfigNode.Parse(ConfigNodeString).GetNode("MODULE"), part);
            Log.dbg("Modes.Length: {0}", ODFC_config.modes.Length - 1);
            updateFuelTexts();

            if (ODFC_config.modes.Length < 2)
            {   // Disable unnecessary UI elements if we only have a single mode
                Events["nextFuel"].guiActive = false;
                Events["nextFuel"].guiActiveEditor = false;
                Fields["fuel_consumption"].guiActive = true; // false;
                Fields["fuel_consumption"].guiActiveEditor = true; // false;
                Actions["previousFuelModeAction"].active = false;
                // Actions["nextFuelModeAction"].active = false;
            }
            else
            {   // If we have at least 2 modes
                if (ODFC_config.modes.Length > 2)
                {   // If we have at least 3 modes
                    Events["previousFuelMode"].guiActive = true;
                    Events["previousFuelMode"].guiActiveEditor = true;
                }
                else
                {   // If we have exactly 2 modes
                    Actions["previousFuelModeAction"].active = false;
                }

                foreach (mode m in ODFC_config.modes)
                {   // Show byproducts tweakable if at least one mode has at least one byproduct
                    if (m.byproducts.Length > 0)
                    {
                        Fields["byproducts"].guiActive = true;
                        Fields["byproducts"].guiActiveEditor = true;
                        break;
                    }
                }
            }
            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        /// <summary>Updates the PAW with scaleFactor and advises KSP that the ship has changed</summary>
        internal void updateEditor() // private
        {
            updateFuelTexts();
            //* following needed to advise KSP that the ship has been modified and it needs to update itself. (Lisias)
            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        /// <summary>Updates the PAW label.</summary>
        internal void updatePAWLabel() // private
        {
            string colorStr = "<#ADFF2F>";
            string begStr = "<size=+1><b>";
            string endStr = "</color></b></size>";

            if (HighLogic.CurrentGame.Parameters.CustomParams<Options>().coloredPAW)
            { colorStr = STATES_COLOUR[(int)state]; }

            if (HighLogic.LoadedSceneIsFlight) PAWStatus = begStr + colorStr + "Fuel Cell: " + status + " - " + ECs_status + " EC/s" + endStr;
            if (HighLogic.LoadedSceneIsEditor) PAWStatus = begStr + colorStr + "Fuel Cell: " + fuel_consumption + " - " + maxECs_status + " EC/s:" + endStr;
        }

        /// <summary>Updates the PAW with scaleFactor and advises KSP that the ship has changed</summary>

        /// <summary>
        ///   <para>
        ///  Called when [rescale].
        /// </para>
        /// </summary>
        /// <param name="scaleFactor">The scale factor.</param>
        internal void OnRescale(TweakScale.ScalingFactor.FactorSet scaleFactor)
        {
            Log.dbg("scaleFactor: {0}", scaleFactor.quadratic);

            /// <summary>this scales any resources on the part with ODFC:  </summary>
            foreach (PartResource resource in this.part.Resources)
            {
                Log.dbg("unscaled resource: {0}: {1} / {2}", resource.resourceName, resource.amount, resource.maxAmount);
                resource.maxAmount *= scaleFactor.quadratic; // .cubic;
                resource.amount *= scaleFactor.quadratic; // cubic;
                Log.dbg("scaled resource: {0}: {1} / {2}", resource.resourceName, resource.amount, resource.maxAmount);
            }

            /// <summary><para>
            /// this scales the actual fuel cell, fuels, byproducts, and maxEC
            /// shouldn't scale rateLimit and threshold because are percentages
            ///</para></summary>
            for (int m = 0; m <= ODFC_config.modes.Length - 1; m++)
            {
                Log.dbg("mode/modes: {0} / {1}", (m + 1), ODFC_config.modes.Length);
                // scale MaxEC
                Log.dbg("unscaled maxEC: {0}", ODFC_config.modes[m].maxEC);
                ODFC_config.modes[m].maxEC *= scaleFactor.quadratic;
                Log.dbg("scaled maxEC: {0}", ODFC_config.modes[m].maxEC);

                // scale fuels in ODFC_config.modes
                Log.dbg("Fuels in mode: {0} / {1}", (m + 1), ODFC_config.modes[m].fuels.Length + 1);
                for (int n = 0; n <= ODFC_config.modes[m].fuels.Length - 1; n++)

                {
                    //Log.dbg("unscaled Fuel: {0} = {1}", PartResourceLibrary.Instance.GetDefinition(ODFC_config.modes[m + 1].fuels[n + 1].resourceID).name, ODFC_config.modes[m + 1].fuels[n + 1].rate);
                    ODFC_config.modes[m].fuels[n].rate *= scaleFactor.quadratic;
                    //Log.dbg("scaled Fuel: {0} = {1}" + PartResourceLibrary.Instance.GetDefinition(ODFC_config.modes[m + 1].fuels[n + 1].resourceID).name, ODFC_config.modes[m + 1].fuels[n + 1].rate);
                }

                // scale byproducts in ODFC_config.modes
                Log.dbg("Byproducts in mode: {0} / {1}", (m + 1), ODFC_config.modes[m].byproducts.Length + 1);
                for (int n = 0; n <= ODFC_config.modes[m].byproducts.Length - 1; n++)

                {
                    //Log.dbg("unscaled byproduct: {0} = {1}", PartResourceLibrary.Instance.GetDefinition(ODFC_config.modes[m].byproducts[n + 1].resourceID).name, ODFC_config.modes[m + 1].byproducts[n + 1].rate);
                    ODFC_config.modes[m].byproducts[n].rate *= scaleFactor.quadratic;
                    //Log.dbg("scaled byproduct: {0} / {1}", PartResourceLibrary.Instance.GetDefinition(ODFC_config.modes[m].byproducts[n + 1].resourceID).name, ODFC_config.modes[m + 1].byproducts[n + 1].rate);
                }
            }
            updateEditor(); // updateFuelTexts(); // removed.this
        }

#endregion Public Functions
#region on events
        /// <summary>Called when part is added to the craft.</summary>
        public override void OnAwake()
        {
            Log.dbg("OnAwake for {0}", this.name);
            base.OnAwake();
        }

        /// <summary>Called when [load].</summary>
        /// <param name="configNode">The configuration node.</param>
        public override void OnLoad(ConfigNode configNode)
        {
            if (string.IsNullOrEmpty(ConfigNodeString))
            {
                this.configNode = configNode;                        // Needed for GetInfo()
                ConfigNodeString = configNode.ToString();            // Needed for marshalling
                Log.dbg("ConfigNodeString: " + ConfigNodeString);
            }
            // base.OnLoad();
        }

        /// <summary>Called when [start].</summary>
        /// <param name="state">The state.</param>
        public override void OnStart(StartState state)
        {
            Log.dbg("OnStart {0}", state);

            if (ElectricChargeID == default(int))
                ElectricChargeID = PartResourceLibrary.Instance.GetDefinition("ElectricCharge").id;

            updateConfig();

            if (state != StartState.Editor)
                part.force_activate();
            // base.OnStart();
        }

        /// <summary>Called when [fixed update].</summary>
        public override void OnFixedUpdate()
        {
            states ns = fuelCellIsEnabled ? states.nominal : states.off;

            if (ns != states.nominal)
            {
                UpdateState(ns, 0, 0);
                return;
            }

            double amount = 0, maxAmount = 0;
            part.GetConnectedResourceTotals(ElectricChargeID, out amount, out maxAmount);

            foreach (PartResource resource in this.part.Resources)
            {
                maxAmount += resource.maxAmount;
                amount += resource.amount;
            }

            double cfTime = TimeWarp.fixedDeltaTime,
                    ECNeed = (Double)(maxAmount * threshold - amount);
                    
                    _fuelModeMaxECRateLimit = ODFC_config.modes[fuelMode].maxEC * rateLimit;

            // add stall code
            if (HighLogic.CurrentGame.Parameters.CustomParams<Options>().needsECtoStart && amount == 0)
            {
                UpdateState(states.stalled, 0, _fuelModeMaxECRateLimit);
                return;
            }

            // Determine activity based on supply/demand
            cfTime = Math.Min(cfTime, ECNeed / _fuelModeMaxECRateLimit);
            if (cfTime <= 0)
            {
                UpdateState(states.noDemand, 0, _fuelModeMaxECRateLimit);
                return;
            }

            // Determine activity based on available fuel
            foreach (Fuel fuel in ODFC_config.modes[fuelMode].fuels)
            {
                amount = 0;
                part.GetConnectedResourceTotals(fuel.resourceID, out amount, out maxAmount);

                foreach (PartResource r in this.part.Resources)
                    amount += r.amount;

                cfTime = Math.Min(cfTime, amount / (fuel.rate * rateLimit));
            }

            if (cfTime == 0)
            {
                UpdateState(states.fuelDeprived, 0, _fuelModeMaxECRateLimit);

                //* this looks for another fuel mode that isn't deprived if autoSwitch == true
                if (HighLogic.CurrentGame.Parameters.CustomParams<Options>().autoSwitch) nextFuelMode();
                return;
            }
            // TODO: responseTime
            /*if (HighLogic.CurrentGame.Parameters.CustomParams<Options>().responseTime >= timeOut)
                  {
                    timeOut = 0;
                }
                else timeOut++;*/

            // Calculate usage based on rate limiting and duty cycle
            double adjr = rateLimit * cfTime;
            //Log.dbg("adjr = rateLimit * cfTime: {1} ", adjr);

            double ECAmount = _fuelModeMaxECRateLimit * cfTime;

            //Log.dbg("ECAmount = _fuelModeMaxECRateLimit * cfTime: {1} ", ECAmount);

            // Don't forget the most important part (add ElectricCharge (EC))
            part.RequestResource(ElectricChargeID, -ECAmount);

            // Commit changes to fuel used
            kommit(ODFC_config.modes[fuelMode].fuels, adjr);

            // Handle byproducts
            kommit(ODFC_config.modes[fuelMode].byproducts, adjr);

            UpdateState(states.nominal, ECAmount / TimeWarp.fixedDeltaTime, _fuelModeMaxECRateLimit);
            base.OnFixedUpdate();
        }

        /// <summary>Updates this instance.</summary>
        public override void OnUpdate()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                double newMax = ODFC_config.modes[fuelMode].maxEC * rateLimit;

                if (lastMax != newMax)
                {
                    lastMax = newMax;
                    maxECs_status = lastMax.ToString(FuelTransferFormat);
                }

                states newState = fuelCellIsEnabled ? states.nominal : states.off;
                UpdateState(newState, newState == states.nominal ? 1 : 0, 1);
            }
            base.OnUpdate();
        }
#endregion on Events
        #region GetInfo

        /// <summary>Formats the information for the part information in the editors.</summary>
        /// <returns>info</returns>
        public override string GetInfo()
        {
            //? this is what is show in the editor
            //? As annoying as it is, pre-parsing the config MUST be done here, because this is called during part loading.
            //? The config is only fully parsed after everything is fully loaded (which is why it's in OnStart())
            if (info == string.Empty)
            {
                ConfigNode[] mds = configNode.GetNodes("MODE");
                info += "Modes: " + mds.Length.ToString();

                for (int n = 0; n < mds.Length; n++)
                    info += "\n\n<color=#99FF00FF>Mode: " + n.ToString() + "</color> - Max EC: " + mds[n].GetValue("MaxEC") +
                        "/s\n<color=#FFFF00FF>Fuels:</color>" + GetResourceRates(mds[n].GetNode("FUELS")) +
                        "\n<color=#FFFF00FF>Byproducts:</color>" + GetResourceRates(mds[n].GetNode("BYPRODUCTS"));
            }
            return info;
        }
#endregion GetInfo
    }
}

#region notes

/*            UIPartActionWindow window = UIPartActionController.Instance.GetItem(part, false);
            UIPartActionGroup group = window.parameterGroups["ODFC"];
            bool collapsed = GameSettings.PAW_COLLAPSED_GROUP_NAMES.Contains("ODFC");
            group.Initialize("ODFC", colorStr + "ODFC: " + statusStr + newDisplayName + colorStrEnd, false, window);*/

/*            UIPartActionWindow window = UIPartActionController.Instance.GetItem(part, false);
                    UIPartActionGroup group = window.parameterGroups["ODFC"];
                    bool collapsed = false; //GameSettings.collpasedPAWGroups.Contains("ODFC");
                    group.Initialize("ODFC", newDisplayName, collapsed, window);*/

/*            ScreenMessages.PostScreenMessage(info, 1, ScreenMessageStyle.UPPER_CENTER, true);
            Debug.Log(info);*/
// DEBUG
//ScreenMessages.PostScreenMessage("a: " + fuels.Length.ToString(), 1, ScreenMessageStyle.LOWER_CENTER, true);

#endregion notes 