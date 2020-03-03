/*
 *   Flight Assist by Naosyth
 *   Email: Naosyth@Gmail.com
 *   
 *   Flight Assist is an ingame program that helps pilots control their vehicles both in atmosphere and in space without having thrust in all six directions.
 *   
 *   For more instructions, see the script's description on the Steam Workshop.
 * 
 */
public const string Version = "3.0.2";

private List<ConfigOption> defaultConfig = new List<ConfigOption>()
{
    new ConfigOption("blockGroupName", "Flight Assist", "Block group that contains all required blocks.", true),
    new ConfigOption("smartDelayTime", "20", "Duration to wait in ticks before overriding gyros in smart mode.", true),
    new ConfigOption("spaceMainThrust", "backward", "Direction of your main thrust used by the vector module.", true),
    new ConfigOption("gyroResponsiveness", "8", "Tuning variable. Lower = faster but may over-shoot more.", true),
    new ConfigOption("maxPitch", "45", "Max pitch used by hover module.", true),
    new ConfigOption("maxRoll", "45", "Max roll used by hover module.", true),
    new ConfigOption("gyroVelocityScale", "0.2", "Tuning variable used to adjust gyroscope response.", true),
    new ConfigOption("startCommand", "hover smart", "Command ran automatically upon successful compilation.", false),
};
private CustomDataConfig configReader;

public IMyShipController cockpit;
public IMyTextPanel textPanel;
public List<IMyGyro> gyros = new List<IMyGyro>();

private GyroController gyroController;
private HoverModule hoverModule;
private VectorModule vectorModule;
private Module activeModule;

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    configReader = new CustomDataConfig(Me, defaultConfig);
    GetBlocks();
    gyroController = new GyroController(gyros, cockpit);
    hoverModule = new HoverModule(configReader, gyroController, cockpit);
    vectorModule = new VectorModule(configReader, gyroController, cockpit);

    string startCommand = configReader.Get<string>("startCommand");
    if (startCommand != null)
        ProcessCommand(startCommand);
    else
        gyroController.SetGyroOverride(false);
}

public void Main(string argument, UpdateType updateType)
{
    if ((updateType & UpdateType.Update1) == 0)
    {
        ProcessCommand(argument);
        return;
    }
    gyroController.Tick();
    activeModule?.Tick();
    if (textPanel != null)
    {
        textPanel.WritePublicText(GetTextPanelHeader());
        if (activeModule != null)
            textPanel.WritePublicText(activeModule?.GetPrintString(), true);
    }
}

private void ProcessCommand(string argument)
{
    string[] args = argument.Split(' ');

    if (args.Length < 1)
        return;

    switch (args[0].ToLower())
    {
        case "hover":
            activeModule = hoverModule;
            break;
        case "vector":
            activeModule = vectorModule;
            break;
        case "stop":
            activeModule = null;
            gyroController.SetGyroOverride(false);
            break;
        case "reset":
            configReader.InitializeConfig();
            break;
    }

    if (activeModule != null)
        activeModule.ProcessCommand(args);
}

private string GetTextPanelHeader()
{
    string header = "    FLIGHT ASSIST V" + Version;
    header += "\n----------------------------------------\n\n";
    return header;
}

private void GetBlocks()
{
    string blockGroupName = configReader.Get<string>("blockGroupName");
    IMyBlockGroup blockGroup = GridTerminalSystem.GetBlockGroupWithName(blockGroupName);

    List<IMyShipController> controllers = new List<IMyShipController>();
    blockGroup.GetBlocksOfType<IMyShipController>(controllers);
    if (controllers.Count == 0)
        throw new Exception("Error: " + blockGroupName + " does not contain a cockpit or remote control block.");
    cockpit = controllers[0];

    List<IMyTextPanel> textPanels = new List<IMyTextPanel>();
    blockGroup.GetBlocksOfType<IMyTextPanel>(textPanels);
    if (textPanels.Count > 0)
    {
        textPanel = textPanels[0];
        textPanel.Font = "Monospace";
        textPanel.FontSize = 1.0f;
        textPanel.ShowPublicTextOnScreen();
    }

    blockGroup.GetBlocksOfType<IMyGyro>(gyros);
    if (gyros.Count == 0)
        throw new Exception("Error: " + blockGroupName + " does not contain any gyroscopes.");
}

public class ConfigOption
{
    public readonly string key;
    public readonly string value;
    public readonly string description;
    public readonly bool required;

    public ConfigOption(string key, string value, string description, bool required)
    {
        this.key = key;
        this.value = value;
        this.description = description;
        this.required = required;
    }
}

public class ConfigParserException: Exception
{
    public ConfigParserException(string message) : base("Config Error: " + message) {}
}

public class CustomDataConfig
{
    private Dictionary<string, string> config = new Dictionary<string, string>();
    private List<ConfigOption> configOptions = new List<ConfigOption>();
    private IMyProgrammableBlock pb;

    public CustomDataConfig(IMyProgrammableBlock pb, List<ConfigOption> configOptions)
    {
        this.pb = pb;
        this.configOptions = configOptions;
        if (pb.CustomData == "")
            InitializeConfig();
        else
            ParseConfig();
    }

    public T Get<T>(string key)
    {
        string value;
        if (config.TryGetValue(key, out value))
            return (T)Convert.ChangeType(value, typeof(T));
        return default(T);
    }

    private void ParseConfig()
    {
        string[] lines = pb.CustomData.Split('\n');

        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var words = line.Split('=');
            if (words.Length == 2)
            {
                string key = words[0].Trim();
                string value = words[1].Trim();
                if (key == "" || value == "")
                    throw new ConfigParserException("Unable to parse line: " + line);
                config[key] = value;
            } else
                throw new ConfigParserException("Unable to parse line: " + line);
        }

        ValidateConfig();
    }

    private void ValidateConfig()
    {
        foreach (ConfigOption configOption in configOptions)
        {
            if (!configOption.required)
                continue;

            if (!config.ContainsKey(configOption.key))
                throw new ConfigParserException("Missing value for required key: " + configOption.key);
        }
    }

    public void InitializeConfig()
    {
        config.Clear();
        StringBuilder configString = new StringBuilder();
        foreach (ConfigOption configOption in configOptions)
        {
            config[configOption.key] = configOption.value;
            configString.Append("# " + configOption.description + " " + (configOption.required ? "Required" : "Optional"));
            configString.Append("\n" + configOption.key + "=" + config[configOption.key] + "\n\n");
        }
        pb.CustomData = configString.ToString();
    }
}

public class GyroController
{
    const double minGyroRpmScale = 0.001;
    const double gyroVelocityScale = 0.2;

    private readonly List<IMyGyro> gyros;
    private readonly IMyShipController cockpit;
    public bool gyroOverride;
    private Vector3D reference;
    private Vector3D target;
    public double angle;

    public GyroController(List<IMyGyro> gyros, IMyShipController cockpit)
    {
        this.gyros = gyros;
        this.cockpit = cockpit;
    }

    public void Tick()
    {
        UpdateGyroRpm();
    }

    public void SetGyroOverride(bool state)
    {
        gyroOverride = state;
        for (int i = 0; i < gyros.Count; i++)
            gyros[i].GyroOverride = gyroOverride;
    }

    public void SetTargetOrientation(Vector3D setReference, Vector3D setTarget)
    {
        reference = setReference;
        target = setTarget;
        UpdateGyroRpm();
    }

    private void UpdateGyroRpm()
    {
        if (!gyroOverride) return;

        for (int i = 0; i < gyros.Count; i++)
        {
            var g = gyros[i];

            Matrix localOrientation;
            g.Orientation.GetMatrix(out localOrientation);
            var localReference = Vector3D.Transform(reference, MatrixD.Transpose(localOrientation));
            var localTarget = Vector3D.Transform(target, MatrixD.Transpose(g.WorldMatrix.GetOrientation()));

            var axis = Vector3D.Cross(localReference, localTarget);
            angle = axis.Length();
            angle = Math.Atan2(angle, Math.Sqrt(Math.Max(0.0, 1.0 - angle * angle)));
            if (Vector3D.Dot(localReference, localTarget) < 0)
                angle = Math.PI;
            axis.Normalize();
            axis *= Math.Max(minGyroRpmScale, g.GetMaximum<float>("Roll") * (angle / Math.PI) * gyroVelocityScale);

            g.Pitch = (float)-axis.X;
            g.Yaw = (float)-axis.Y;
            g.Roll = (float)-axis.Z;
        }
    }
}

public class Helpers
{
    public const double halfPi = Math.PI / 2;
    public const double radToDeg = 180 / Math.PI;
    public const double degToRad = Math.PI / 180;

    public static double NotNan(double val)
    {
        if (double.IsNaN(val))
            return 0;
        return val;
    }

    public static bool EqualWithMargin(double value, double target, double margin)
    {
        return value > target - margin && value < target + margin;
    }

    public static string GetCommandFromArgs(string[] args)
    {
        if (args.Length < 2 || args[1] == null)
            return "";
        return args[1].ToLower();
    }

    public static void PrintException(string error)
    {
        throw new Exception("Error: " + error);
    }
}

public class HoverModule : Module
{
    private int gyroResponsiveness;
    private double maxPitch;
    private double maxRoll;

    private readonly CustomDataConfig config;
    private readonly GyroController gyroController;
    private readonly IMyShipController cockpit;
    private int smartDelayTimer;
    private float setSpeed;
    private double worldSpeedForward, worldSpeedRight, worldSpeedUp;
    private double pitch, roll;
    private double desiredPitch, desiredRoll;

    public HoverModule(CustomDataConfig config, GyroController gyroController, IMyShipController cockpit)
    {
        this.config = config;
        this.gyroController = gyroController;
        this.cockpit = cockpit;

        gyroResponsiveness = config.Get<int>("gyroResponsiveness");
        maxPitch = config.Get<double>("maxPitch");
        maxRoll = config.Get<double>("maxRoll");

        AddAction("disabled", (args) => { gyroController.SetGyroOverride(false); }, null);

        AddAction("smart", (string[] args) =>
        {
            smartDelayTimer = 0;
            setSpeed = (args.Length > 0 && args[0] != null) ? Int32.Parse(args[0]) : 0;
        }, () =>
        {
            if (cockpit.MoveIndicator.Length() > 0.0f || cockpit.RotationIndicator.Length() > 0.0f)
            {
                desiredPitch = -(pitch - 90);
                desiredRoll = (roll - 90);
                gyroController.SetGyroOverride(false);
                smartDelayTimer = 0;
            } else if (smartDelayTimer > config.Get<int>("smartDelayTime"))
            {
                gyroController.SetGyroOverride(true);
                desiredPitch = Math.Atan((worldSpeedForward - setSpeed) / gyroResponsiveness) / Helpers.halfPi * maxPitch;
                desiredRoll = Math.Atan(worldSpeedRight / gyroResponsiveness) / Helpers.halfPi * maxRoll;
            } else
                smartDelayTimer++;
        });

        AddAction("stop", null, () =>
        {
            desiredPitch = Math.Atan(worldSpeedForward / gyroResponsiveness) / Helpers.halfPi * maxPitch;
            desiredRoll = Math.Atan(worldSpeedRight / gyroResponsiveness) / Helpers.halfPi * maxRoll;
        });

        AddAction("glide", null, () =>
        {
            desiredPitch = 0;
            desiredRoll = Math.Atan(worldSpeedRight / gyroResponsiveness) / Helpers.halfPi * maxRoll;
        });

        AddAction("freeglide", null, () =>
        {
            desiredPitch = 0;
            desiredRoll = 0;
        });
    }

    protected override void OnSetAction()
    {
        gyroController.SetGyroOverride(action?.execute != null);
        if (action?.execute != null)
            cockpit.DampenersOverride = true;
    }

    public override void Tick()
    {
        base.Tick();

        if (cockpit.GetNaturalGravity().Length() == 0)
            SetAction("disabled");

        CalcWorldSpeed();
        CalcPitchAndRoll();
        PrintStatus();
        if (cockpit.GetNaturalGravity().Length() > 0)
        {
            PrintVelocity();
            PrintOrientation();
        } else
            PrintLine("\n\n   No Planetary Gravity");

        if (action?.execute != null)
            action?.execute();
        if (gyroController.gyroOverride)
            ExecuteManeuver();
    }

    private void PrintStatus()
    {
        PrintLine("    HOVER MODULE ACTIVE");
        PrintLine("    MODE: " + action?.name.ToUpper());
        if (setSpeed > 0)
            PrintLine("    SET SPEED: " + setSpeed + "m/s");
        else
            PrintLine("");
    }

    private void PrintVelocity()
    {
        string velocityString = " X:" + worldSpeedForward.ToString("+000;\u2013000");
        velocityString += " Y:" + worldSpeedRight.ToString("+000;\u2013000");
        velocityString += " Z:" + worldSpeedUp.ToString("+000;\u2013000");
        PrintLine("\n Velocity (m/s)+\n" + velocityString);
    }

    private void PrintOrientation()
    {
        PrintLine("\n Orientation");
        PrintLine(" Pitch: " + (90-pitch).ToString("+00;\u201300") + "° | Roll: " + ((90-roll)*-1).ToString("+00;\u201300") + "°");
    }

    private void CalcWorldSpeed()
    {
        Vector3D linearVelocity = Vector3D.Normalize(cockpit.GetShipVelocities().LinearVelocity);
        Vector3D gravity = -Vector3D.Normalize(cockpit.GetNaturalGravity());
        worldSpeedForward = Helpers.NotNan(Vector3D.Dot(linearVelocity, Vector3D.Cross(gravity, cockpit.WorldMatrix.Right)) * cockpit.GetShipSpeed());
        worldSpeedRight = Helpers.NotNan(Vector3D.Dot(linearVelocity, Vector3D.Cross(gravity, cockpit.WorldMatrix.Forward)) * cockpit.GetShipSpeed());
        worldSpeedUp = Helpers.NotNan(Vector3D.Dot(linearVelocity, gravity) * cockpit.GetShipSpeed());
    }

    private void CalcPitchAndRoll()
    {
        Vector3D gravity = -Vector3D.Normalize(cockpit.GetNaturalGravity());
        pitch = Helpers.NotNan(Math.Acos(Vector3D.Dot(cockpit.WorldMatrix.Forward, gravity)) * Helpers.radToDeg);
        roll = Helpers.NotNan(Math.Acos(Vector3D.Dot(cockpit.WorldMatrix.Right, gravity)) * Helpers.radToDeg);
    }

    private void ExecuteManeuver()
    {
        Matrix cockpitOrientation;
        cockpit.Orientation.GetMatrix(out cockpitOrientation);
        var quatPitch = Quaternion.CreateFromAxisAngle(cockpitOrientation.Left, (float)(desiredPitch * Helpers.degToRad));
        var quatRoll = Quaternion.CreateFromAxisAngle(cockpitOrientation.Backward, (float)(desiredRoll * Helpers.degToRad));
        var reference = Vector3D.Transform(cockpitOrientation.Down, quatPitch * quatRoll);
        gyroController.SetTargetOrientation(reference, cockpit.GetNaturalGravity());
    }
}

public abstract class Module
{
    protected ModuleAction action;
    protected Dictionary<string, ModuleAction> actions = new Dictionary<string, ModuleAction>();
    private string printBuffer;

    public virtual void Tick() { printBuffer = ""; }

    public virtual void ProcessCommand(string[] args) {
        SetAction(Helpers.GetCommandFromArgs(args), args.Skip(2).ToArray());
    }

    protected void AddAction(string name, Action<string[]> initialize, Action execute)
    {
        actions.Add(name, new ModuleAction(name, initialize, execute));
    }

    protected bool SetAction(string actionName)
    {
        return SetAction(actionName, null);
    }

    protected bool SetAction(string actionName, string[] args)
    {
        if (!actions.Keys.Contains<string>(actionName))
            return false;

        if (actions.TryGetValue(actionName, out action))
        {
            action.initialize?.Invoke(args);
            OnSetAction();
            return true;
        }
        return false;
    }

    protected virtual void OnSetAction() { }

    public string GetPrintString() { return printBuffer; }
    protected void PrintLine(string line) { printBuffer += line + "\n"; }
}

public class ModuleAction
{
    public string name;
    public Action<string[]> initialize;
    public Action execute;

    public ModuleAction(string name, Action<string[]> initialize, Action execute)
    {
        this.name = name;
        this.initialize = initialize;
        this.execute = execute;
    }
}

public class VectorModule : Module
{
    private double angleThreshold = 0.01;
    private double speedThreshold = 0.3;

    private readonly GyroController gyroController;
    private readonly IMyShipController cockpit;
    private Vector3D thrustVector;

    private double startSpeed;

    public VectorModule(CustomDataConfig config, GyroController gyroController, IMyShipController cockpit)
    {
        this.gyroController = gyroController;
        this.cockpit = cockpit;

        thrustVector = GetThrustVector(config.Get<string>("spaceMainThrust"));

        AddAction("disabled", (args) => { gyroController.SetGyroOverride(false); }, null);
        AddAction("brake", (args) => {
            startSpeed = cockpit.GetShipSpeed();
            cockpit.DampenersOverride = false;
        }, SpaceBrake);
        AddAction("prograde", null, () => { TargetOrientation(-Vector3D.Normalize(cockpit.GetShipVelocities().LinearVelocity)); });
        AddAction("retrograde", null, () => { TargetOrientation(Vector3D.Normalize(cockpit.GetShipVelocities().LinearVelocity)); });
    }

    protected override void OnSetAction()
    {
        gyroController.SetGyroOverride(action?.execute != null);
    }

    public override void Tick()
    {
        base.Tick();

        PrintStatus();

        if (gyroController.gyroOverride)
            action?.execute();
    }

    private void PrintStatus()
    {
        PrintLine("  VECTOR MODULE ACTIVE");
        PrintLine("  MODE: " + action?.name.ToUpper() + "\n");

        string output = "";
        if (action?.name == "brake")
        {
            var percent = Math.Abs(cockpit.GetShipSpeed() / startSpeed);
            string progressBar;
            progressBar = "|";
            int width = 24;
            var height = 3;
            output = " PROGRESS\n";
            for (var i = 0; i < width; i++)
                progressBar += (i < width * (1-percent)) ? "#" : " ";
            progressBar += "|\n";
            for (var i = 0; i < height; i++)
                output += progressBar;
        }
        else
            output = " Speed: " + Math.Abs(cockpit.GetShipSpeed()).ToString("000") + " m/s";

        PrintLine(output);
    }

    private void TargetOrientation(Vector3D target)
    {
        gyroController.SetTargetOrientation(thrustVector, target);
    }

    private void SpaceBrake()
    {
        TargetOrientation(Vector3D.Normalize(cockpit.GetShipVelocities().LinearVelocity));

        if (Helpers.EqualWithMargin(gyroController.angle, 0, angleThreshold))
            cockpit.DampenersOverride = true;

        if (cockpit.GetShipSpeed() < speedThreshold)
            SetAction("disabled");
    }

    private Vector3D GetThrustVector(string direction)
    {
        Matrix cockpitOrientation;
        cockpit.Orientation.GetMatrix(out cockpitOrientation);
        switch (direction.ToLower())
        {
            case "down": return cockpitOrientation.Down;
            case "up": return cockpitOrientation.Up;
            case "forward": return cockpitOrientation.Forward;
            case "backward": return cockpitOrientation.Backward;
            case "right": return cockpitOrientation.Right;
            case "left": return cockpitOrientation.Left;
            default: throw new Exception("Unidentified thrust direction '" + direction.ToLower() + "'");
        }
    }
}
