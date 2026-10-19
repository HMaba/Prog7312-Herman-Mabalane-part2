using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartXApi.Models
{
    public enum DeploymentNodeType
    {
        Farm,
        Area,
        Node,
        Sensor
    }
    public class DeploymentNode
    {
        public string Name { get; set; } = string.Empty;
        public DeploymentNodeType NodeType { get; set; }
        public List<DeploymentNode> Children { get; set; } = new();
    }
}