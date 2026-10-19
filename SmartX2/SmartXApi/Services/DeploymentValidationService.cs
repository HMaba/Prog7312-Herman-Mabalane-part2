using SmartXApi.Models;
using SmartXApi.Data;
using SmartXApi.Controllers;
using SmartXApi.Services;

namespace SmartXApi.Services
{

    public interface IDeploymentValidationService
    {
        TelemetryResult ValidateTree(DeploymentNode root);
        int CountSensors(DeploymentNode root);
    }

    public class DeploymentValidationService : IDeploymentValidationService
    {
        private static readonly Dictionary<DeploymentNodeType, DeploymentNodeType[]> AllowedChildTypes = new()
        {
            [DeploymentNodeType.Farm] = new[] { DeploymentNodeType.Area },
            [DeploymentNodeType.Area] = new[] { DeploymentNodeType.Node },
            [DeploymentNodeType.Node] = new[] { DeploymentNodeType.Sensor },
            [DeploymentNodeType.Sensor] = Array.Empty<DeploymentNodeType>()
        };

        public TelemetryResult ValidateTree(DeploymentNode root)
        {
            var errors = new List<string>();
            ValidateRecursive(root, errors);

            return errors.Count == 0
                ? new TelemetryResult { IsValid = true, Message = "Deployment structure is valid." }
                : new TelemetryResult { IsValid = false, Message = string.Join("; ", errors) };
        }

        private void ValidateRecursive(DeploymentNode node, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(node.Name))
            {
                errors.Add($"A {node.NodeType} node is missing a name.");
            }

            var allowedChildren = AllowedChildTypes[node.NodeType];

            foreach (var child in node.Children)
            {
                if (!allowedChildren.Contains(child.NodeType))
                {
                    errors.Add($"{node.NodeType} '{node.Name}' cannot directly contain a {child.NodeType} ('{child.Name}').");
                }

                
                ValidateRecursive(child, errors);
            }
        }

       
        public int CountSensors(DeploymentNode root)
        {
            if (root.NodeType == DeploymentNodeType.Sensor)
            {
                return 1;
            }

            var count = 0;
            foreach (var child in root.Children)
            {
                count += CountSensors(child);
            }

            return count;
        }
    }
}