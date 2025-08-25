using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.AI
{
    public class Voxel
    {
        public Vector3 Position { get; private set; }
        public float Radius { get; private set; }
        public float Height { get; private set; }

        public float Length { get; private set; }

        public bool IsWalkable { get; set; } = false;

        public Voxel(Vector3 position, float radius, float height)
        {
            Position = position;
            Radius = radius;
            Length = radius;
            Height = height;
        }
    }
}
