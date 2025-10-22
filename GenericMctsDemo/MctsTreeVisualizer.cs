using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GenericMcts
{
    /// <summary>
    /// Utilities for visualizing MCTS tree structures
    /// </summary>
    public static class MctsTreeVisualizer
    {
        /// <summary>
        /// Export tree to DOT format (Graphviz)
        /// </summary>
        public static string ToDot<TState, TAction>(
            Node<TState, TAction> root,
            Func<TState, string> stateFormatter,
            Func<TAction?, string> actionFormatter,
            int maxDepth = 3,
            int minVisits = 1)
        {
            var sb = new StringBuilder();
            sb.AppendLine("digraph MCTS {");
            sb.AppendLine("  rankdir=TB;");
            sb.AppendLine("  node [shape=box, style=rounded];");
            
            var nodeId = 0;
            var visited = new Dictionary<Node<TState, TAction>, int>();
            
            void AddNode(Node<TState, TAction> node, int depth)
            {
                if (depth > maxDepth || node.Visits < minVisits) return;
                
                if (!visited.TryGetValue(node, out var id))
                {
                    id = nodeId++;
                    visited[node] = id;
                    
                    var label = new StringBuilder();
                    label.Append($"Visits: {node.Visits}\\n");
                    label.Append($"Value: {node.TotalValue:F1}\\n");
                    label.Append($"Mean: {(node.Visits > 0 ? node.TotalValue / node.Visits : 0):F3}\\n");
                    label.Append($"Kind: {node.Kind}\\n");
                    if (node.Untried.Count > 0)
                        label.Append($"Untried: {node.Untried.Count}\\n");
                    label.Append("---\\n");
                    label.Append(stateFormatter(node.State).Replace("\"", "\\\""));
                    
                    var color = node.Kind switch
                    {
                        NodeKind.Decision => "lightblue",
                        NodeKind.Chance => "lightgreen",
                        NodeKind.Terminal => "lightcoral",
                        _ => "white"
                    };
                    
                    sb.AppendLine($"  node{id} [label=\"{label}\", fillcolor={color}, style=\"rounded,filled\"];");
                }
                
                foreach (var child in node.Children.Where(c => c.Visits >= minVisits))
                {
                    AddNode(child, depth + 1);
                    
                    // Only add edge if child was actually added (not pruned by maxDepth)
                    if (visited.TryGetValue(child, out var childId))
                    {
                        var actionLabel = actionFormatter(child.IncomingAction);
                        var edgeLabel = $"{actionLabel}\\nV:{child.Visits}";
                        sb.AppendLine($"  node{id} -> node{childId} [label=\"{edgeLabel}\"];");
                    }
                }
            }
            
            AddNode(root, 0);
            sb.AppendLine("}");
            return sb.ToString();
        }
        
        /// <summary>
        /// Export tree to simple text format
        /// </summary>
        public static string ToText<TState, TAction>(
            Node<TState, TAction> root,
            Func<TState, string> stateFormatter,
            Func<TAction?, string> actionFormatter,
            int maxDepth = 3,
            int minVisits = 1)
        {
            return ToText(root, stateFormatter, (state, action) => actionFormatter(action), maxDepth, minVisits);
        }
        
        /// <summary>
        /// Export tree to simple text format with state-aware action formatting
        /// </summary>
        public static string ToText<TState, TAction>(
            Node<TState, TAction> root,
            Func<TState, string> stateFormatter,
            Func<TState, TAction?, string> actionFormatter,
            int maxDepth = 3,
            int minVisits = 1)
        {
            var sb = new StringBuilder();
            
            void PrintNode(Node<TState, TAction> node, string indent, int depth, bool isLast)
            {
                if (depth > maxDepth || node.Visits < minVisits) return;
                
                var prefix = depth == 0 ? "" : (isLast ? "\\-- " : "|-- ");
                // For action formatting, use the PARENT's state (the state before the action was taken)
                var parentState = node.Parent != null ? node.Parent.State : node.State;
                var action = depth == 0 ? "ROOT" : actionFormatter(parentState, node.IncomingAction);
                
                // Collect chain of single-child nodes for inline display
                var actionChain = new List<string> { action };
                var current = node;
                var currentDepth = depth;
                
                while (currentDepth < maxDepth)
                {
                    var children = current.Children.Where(c => c.Visits >= minVisits).ToList();
                    if (children.Count != 1) break;
                    
                    var child = children[0];
                    currentDepth++;
                    // Use current node's state as the "before" state for the child's action
                    actionChain.Add(actionFormatter(current.State, child.IncomingAction));
                    current = child;
                }
                
                // Print the chain with stats from the final node
                var mean = current.Visits > 0 ? current.TotalValue / current.Visits : 0;
                var chainStr = string.Join(" -> ", actionChain);
                sb.AppendLine($"{indent}{prefix}{chainStr} [V:{current.Visits} W:{current.TotalValue:F1} Q:{mean:F3}]");
                
                // Print children of the last node in the chain (if it has multiple children)
                if (currentDepth < maxDepth)
                {
                    var children = current.Children.Where(c => c.Visits >= minVisits).ToList();
                    if (children.Count > 1)
                    {
                        for (int i = 0; i < children.Count; i++)
                        {
                            var newIndent = indent + (depth == 0 ? "" : (isLast ? "    " : "|   "));
                            PrintNode(children[i], newIndent, currentDepth + 1, i == children.Count - 1);
                        }
                    }
                }
            }
            
            PrintNode(root, "", 0, true);
            return sb.ToString();
        }
        
        /// <summary>
        /// Get tree statistics
        /// </summary>
        public static TreeStats GetStats<TState, TAction>(Node<TState, TAction> root)
        {
            var stats = new TreeStats();
            var queue = new Queue<Node<TState, TAction>>();
            queue.Enqueue(root);
            
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                stats.TotalNodes++;
                stats.TotalVisits += node.Visits;
                
                if (node.Children.Count == 0)
                    stats.LeafNodes++;
                
                stats.MaxDepth = Math.Max(stats.MaxDepth, GetDepth(node));
                
                foreach (var child in node.Children)
                    queue.Enqueue(child);
            }
            
            return stats;
        }
        
        private static int GetDepth<TState, TAction>(Node<TState, TAction> node)
        {
            int depth = 0;
            var current = node;
            while (current.Parent != null)
            {
                depth++;
                current = current.Parent;
            }
            return depth;
        }
    }
    
    public class TreeStats
    {
        public int TotalNodes { get; set; }
        public int LeafNodes { get; set; }
        public int TotalVisits { get; set; }
        public int MaxDepth { get; set; }
        
        public override string ToString()
        {
            return $"Nodes: {TotalNodes}, Leaves: {LeafNodes}, Visits: {TotalVisits}, MaxDepth: {MaxDepth}";
        }
    }
}
