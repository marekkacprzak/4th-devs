namespace EvaluationAgent.Models;

[Flags]
public enum AnomalyType
{
    None = 0,
    OutOfRange = 1,
    InactiveSensorNonZero = 2,
    OperatorFalseOk = 4,
    OperatorFalseError = 8
}
