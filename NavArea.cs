using Assets.Scripts.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.AI;
using UnityEngine.Rendering;

namespace Assets.Scripts.AI
{

    public class Edge : IEquatable<Edge>
    {
        public Edge(Vector3 start, Vector3 end)
        {
            this.start = start;
            this.end = end;
        }


        public List<Edge> connectedEdges = new List<Edge>();
        public Vector3 start;
        public Vector3 end;
        public Vector3 Direction => (end - start).normalized;
        public float Length => Vector3.Distance(start, end);

        public void Draw(Color color)
        {
            Gizmos.color = color;
            Gizmos.DrawLine(start, end);
        }


        public bool IsConnected(Edge other)
        {
            return start == other.start || start == other.end || end == other.start || end == other.end;
        }

        public bool IsCollinear(Edge other)
        {
            float dot = Vector3.Dot(this.Direction, other.Direction);
            return Mathf.Abs(dot - 1) < 0.001f; // Collinear if dot product is close to 1
        }
        public bool Equals(Edge other)
        {
            return (start == other.start && end == other.end) || (start == other.end && end == other.start);
        }
    }

    public struct Polygon
    {
        public List<Edge> edges;
        public List<Vector3> Vertices
        {
            get
            {
                HashSet<Vector3> verts = new HashSet<Vector3>();
                foreach (var edge in edges)
                {
                    verts.Add(edge.start);
                    verts.Add(edge.end);
                }
                return verts.ToList();
            }
        }
        public Polygon(List<Edge> edges)
        {
            this.edges = edges;
        }

    }

    public class NavArea : MonoBehaviour
    {


        [SerializeField] private float _width;
        [SerializeField] private float _length;
        [SerializeField] private float _height;
        [SerializeField] private float _agentRadius;
        [SerializeField] private float _agentHeight;
        [SerializeField] private float _walkableAngle = 45.0f;

        private List<Voxel> _voxels = new List<Voxel>();
        private List<Edge> _edges = new List<Edge>();
        Dictionary<Vector3Int, Voxel> _voxelGrid = new Dictionary<Vector3Int, Voxel>();
        private bool[,] _walkable;
        private List<Polygon> _polygons = new List<Polygon>();


        static readonly int[][] s_MarchingSquaresTable = new int[][]
       {
            new int[] {},           // 0000
            new int[] {0,1},        // 0001
            new int[] {1,2},        // 0010
            new int[] {0,2},        // 0011
            new int[] {2,3},        // 0100
            new int[] {0,1,2,3},    // 0101
            new int[] {1,3},        // 0110
            new int[] {0,3},        // 0111
            new int[] {0,3},        // 1000
            new int[] {1,3},        // 1001
            new int[] {0,1,2,3},    // 1010 
            new int[] {2,3},        // 1011
            new int[] {0,2},        // 1100
            new int[] {1,2},        // 1101
            new int[] {0,1},        // 1110
            new int[] {}            // 1111
       };

        private void Awake()
        {

        }

        private void Start()
        {
            FillVoxels();
            BuildContour();
            MergeEdges();
            MarkConnectedEdges();
            BuildPolygons();

        }

      
        private void MarkConnectedEdges()
        {
            for(int i = 0; i < _edges.Count; i++)
            {
                Edge edge = _edges[i];
                for(int j = 0; j < _edges.Count; j++)
                {

                    if (i == j)
                        continue;

                    if (edge.IsConnected(_edges[j]))
                    {
                        edge.connectedEdges.Add(_edges[j]);
                    }
                }
            }
            
        }

        private void BuildPolygons()
        {
            //just move in one direction until you get back to the start
            HashSet<Edge> visitedEdges = new HashSet<Edge>();
            foreach(var edge in _edges)
            {
                if (visitedEdges.Contains(edge))
                    continue;
                List<Edge> polygonEdges = new List<Edge>();
                Edge currentEdge = edge;
                polygonEdges.Add(currentEdge);
                visitedEdges.Add(currentEdge);
                while (true)
                {
                    //Find the next connected edge that hasn't been visited
                    Edge nextEdge = null;
                    foreach(var connected in currentEdge.connectedEdges)
                    {
                        if (!visitedEdges.Contains(connected))
                        {
                            nextEdge = connected;
                            break;
                        }
                    }
                    if (nextEdge == null || nextEdge.Equals(edge))
                        break; // No unvisited connected edges or looped back to start
                    polygonEdges.Add(nextEdge);
                    visitedEdges.Add(nextEdge);
                    currentEdge = nextEdge;
                }
                if (polygonEdges.Count > 2) // A valid polygon needs at least 3 edges
                {
                    _polygons.Add(new Polygon(polygonEdges));
                }
            }

        }

        private void BuildContour()
        {
            int sizeX = (int)(_width / _agentRadius);
            int sizeZ = (int)(_length / _agentRadius);
            int sizeY = (int)(_height / _agentHeight);

            float agentX = _agentRadius / 2.0f;
            float agentZ = _agentRadius / 2.0f;


            Vector3 topLeft = transform.position + new Vector3(-_width, 0, -_length) / 2.0f;

            for (int x = 0; x < sizeX - 1; x++)
            {

                for (int z = 0; z < sizeZ - 1; z++)
                {
                    // Build state (bit mask) from walkable corners
                    int state = 0;
                    if (_walkable[x, z]) state |= 1;        // bottom-left
                    if (_walkable[x + 1, z]) state |= 2;    // bottom-right
                    if (_walkable[x + 1, z + 1]) state |= 4; // top-right
                    if (_walkable[x, z + 1]) state |= 8;    // top-left

                    // pick edges from lookup table
                    var edges = s_MarchingSquaresTable[state];
                    for (int i = 0; i < edges.Length; i += 2)
                    {
                        Vector2 p1 = GetEdgeMidpoint(edges[i], x, z);
                        Vector2 p2 = GetEdgeMidpoint(edges[i + 1], x, z);

                        Vector3 wp1 = topLeft + new Vector3(p1.x, 0.0f, p1.y);
                        Vector3 wp2 = topLeft + new Vector3(p2.x, 0.0f, p2.y);

                        wp1 = AdjustPointToSurface(wp1);
                        wp2 = AdjustPointToSurface(wp2);

                        //Fix their heights
                        wp1.y += 1f;
                        wp2.y += 1f;

                        Edge edge = new Edge(wp1, wp2);
                        _edges.Add(edge);
                    }
                }

            }
        }

   

        private void MergeEdges()
        {
            //Merge edges
            Debug.Log("There are " + _edges.Count + " edges before simplification.");
            Edge previous;
            Edge current = _edges[0];

            List<Edge> simplifiedEdges = new List<Edge>();

            for (int i = 1; i < _edges.Count; i++)
            {
                previous = current;
                current = _edges[i];
                if (previous.IsCollinear(current) && Vector3.Distance(previous.end, current.start) < 0.1f)
                {
                    // Merge edges
                    current = new Edge(previous.start, current.end);
                }
                else
                {
                    simplifiedEdges.Add(previous);
                    _edges[i] = previous;
                }
            }
            simplifiedEdges.Add(current); // Add the last edge
            _edges = simplifiedEdges;

            Debug.Log("There are " + _edges.Count + " edges after simplification.");
        }

        Vector3 AdjustPointToSurface(Vector3 flatPoint)
        {
            // Cast a ray down from above the contour point
            Ray ray = new Ray(flatPoint + Vector3.up * _agentHeight, Vector3.down);
            if (Physics.SphereCast(ray, _agentRadius, out RaycastHit hit, 50f))
            {
               
                return hit.point; // Snap contour point to the surface
            }

            return flatPoint; // Fallback (no hit found)
        } 

        private Vector2 GetEdgeMidpoint(int edgeIndex, int x, int z)
        {
            float cell = _agentRadius; // horizontal step
            switch (edgeIndex)
            {
                case 0: // left edge
                    return new Vector2(x * cell, (z + 0.5f) * cell);
                case 1: // bottom edge
                    return new Vector2((x + 0.5f) * cell, z * cell);
                case 2: // right edge
                    return new Vector2((x + 1) * cell, (z + 0.5f) * cell);
                case 3: // top edge
                    return new Vector2((x + 0.5f) * cell, (z + 1) * cell);
                default:
                    return Vector2.zero;
            }
        }


        private void Update()
        {

        }


        private void FillVoxels()
        {
            int numOfCubesX = (int)(_width / _agentRadius);
            int numOfCubesY = (int)(_height / _agentHeight);
            int numOfCubesZ = (int)(_length / _agentRadius);

            _walkable = new bool[numOfCubesX, numOfCubesZ];


            float agentX = _agentRadius / 2.0f;
            float agentZ = _agentRadius / 2.0f;
            float agentY = -_agentHeight / 2.0f;

            Vector3 topLeft = transform.position + new Vector3(-_width, _height, -_length) / 2.0f;


            for (int x = 0; x < numOfCubesX; x++)
            {
                for (int y = 0; y < numOfCubesY; y++)
                {

                    for (int z = 0; z < numOfCubesZ; z++)
                    {
                        Vector3 position = topLeft + new Vector3(x * _agentRadius, y * -_agentHeight, z * _agentRadius);
                        Voxel voxel = new Voxel(
                            position,
                            0.1f,
                            0.1f
                        );

                        Vector3 upPos = position + Vector3.up * _agentHeight * 0.5f;

                        if (Physics.Raycast(upPos, Vector3.down, out RaycastHit hitinfo1, _agentHeight))
                        {

                            float angle;
                            bool isWalkable = true;

                            RaycastHit hitInfo;

                            //Check sides
                            if (Physics.Raycast(upPos + Vector3.right * _agentRadius * 0.5f, Vector3.right, out hitInfo, _agentRadius + 0.1f))
                            {
                                angle = Vector3.Angle(hitInfo.normal, Vector3.up);
                                if (angle > _walkableAngle)
                                    isWalkable = false;
                            }
                            else if (Physics.Raycast(upPos + Vector3.forward * _agentRadius * 0.5f, Vector3.forward, out hitInfo, _agentRadius + 0.1f))
                            {
                                angle = Vector3.Angle(hitInfo.normal, Vector3.up);
                                if (angle > _walkableAngle)
                                    isWalkable = false;

                            }
                            else if (Physics.Raycast(upPos + Vector3.back * _agentRadius * 0.5f, Vector3.back, out hitInfo, _agentRadius + 0.1f))
                            {
                                angle = Vector3.Angle(hitInfo.normal, Vector3.up);
                                if (angle > _walkableAngle)
                                    isWalkable = false;
                            }
                            else if (Physics.Raycast(upPos + Vector3.left * _agentRadius * 0.5f, Vector3.left, out hitInfo, _agentRadius + 0.1f))
                            {
                                angle = Vector3.Angle(hitInfo.normal, Vector3.up);
                                if (angle > _walkableAngle)
                                    isWalkable = false;
                            }
                            //Check diagonals
                            else if (Physics.Raycast(upPos + (Vector3.left + Vector3.forward).normalized * _agentRadius * 0.5f, (Vector3.left + Vector3.forward).normalized, out hitInfo, _agentRadius + 0.5f))
                            {
                                angle = Vector3.Angle(hitInfo.normal, Vector3.up);
                                if (angle > _walkableAngle)
                                    isWalkable = false;
                            }
                            else if (Physics.Raycast(upPos + (Vector3.back + Vector3.left).normalized * _agentRadius * 0.5f, (Vector3.back + Vector3.left).normalized, out hitInfo, _agentRadius + 0.5f))
                            {
                                angle = Vector3.Angle(hitInfo.normal, Vector3.up);
                                if (angle > _walkableAngle)
                                    isWalkable = false;
                            }
                            else if (Physics.Raycast(upPos + (Vector3.back + Vector3.right).normalized * _agentRadius * 0.5f, (Vector3.back + Vector3.right).normalized, out hitInfo, _agentRadius + 0.5f))
                            {
                                angle = Vector3.Angle(hitInfo.normal, Vector3.up);
                                if (angle > _walkableAngle)
                                    isWalkable = false;
                            }
                            else if (Physics.Raycast(upPos + (Vector3.right + Vector3.forward).normalized * _agentRadius * 0.5f, (Vector3.right + Vector3.forward).normalized, out hitInfo, _agentRadius + 0.5f))
                            {
                                angle = Vector3.Angle(hitInfo.normal, Vector3.up);
                                if (angle > _walkableAngle)
                                    isWalkable = false;
                            }


                                angle = Vector3.Angle(hitinfo1.normal, Vector3.up);
                            if (angle > _walkableAngle)
                                isWalkable = false;

                            voxel.IsWalkable = isWalkable;
                            _walkable[x, z] = isWalkable;
                        }
                        if (voxel.IsWalkable)
                        {
                            _voxelGrid.Add(new Vector3Int(x, y, z), voxel);
                            _voxels.Add(voxel);
                        }
                    }
                }
            }

        }


        private void OnDrawGizmos()
        {

            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(transform.position, new Vector3(_width, _height, _length));


            if (_edges == null || _edges.Count == 0)
                return;

           

            Gizmos.color = Color.red;

            for (int i = 0; i < _edges.Count; i++)
            {
                _edges[i].Draw(Color.red);
            }




        }

    }
}

