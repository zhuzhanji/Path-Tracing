using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BVH_Node {
    public bool leaf = false;
    public BBox bb = BBox.EmptyBBox();
    public BVH_Node left = null, right = null, parent = null;
    public List<Triangle> triangles = null;

    public static BVH_Node BuildBVH(List<Triangle> tris) {
        return BuildBVHNode(tris, 0);
    }

    public static BVH_Node BuildBVHNode(List<Triangle> tris, uint depth) {
        if (depth > 60) { return BVH_Node.BuildLeafNode(tris); }  // max depth reached
        if (tris.Count < 4)
            return BVH_Node.BuildLeafNode(tris);
        
        float minCost = float.MaxValue;
        Axis bestAxis = Axis.NONE;
        float bestSplitPos = 0f; // where on the axis to split

        BBox trisBB = BBox.BBoxFromTris(tris);

        uint numBins = (uint) Mathf.Max(32, 1024 / Mathf.Pow(2, depth));

        foreach (Axis axis in new Axis[] {Axis.X, Axis.Y, Axis.Z}) {
            float bbAxisWidth = trisBB.getWidth(axis);
            float bbAxisStart = trisBB.GetAxisStart(axis);
            if (bbAxisWidth < 1e-6)  // too narrow, don't split here
                continue;

            // calculate step size
            float stepSize = bbAxisWidth / (float) numBins;
                // scale down bin number by a factor of 2 at each successive BVH layer
                    // 1024 --> 512 --> 256 --> ... --> 32
            BBox[] binBBs = new BBox[numBins];  // track bin bounding boxes
            uint[] binCounts = new uint[numBins];  // track bin counts

            // assign triangles to bins
            foreach (var tri in tris)
            {
                // get triangle center along axis
                float triCenter = tri.bb.getCenter(axis);
                // calculate which bin it falls into
                int triBin = (int) Mathf.Min(numBins - 1,  Mathf.FloorToInt((triCenter - bbAxisStart) / stepSize));
                // Debug.Log(string.Format("triBin: {0}, numBins: {1}", triBin, numBins));
                // update bin data
                binBBs[triBin] = (binBBs[triBin] ?? BBox.EmptyBBox()).Union(tri.bb);
                binCounts[triBin] += 1;
            }

            // test all split points
            for (int i = 0; i < numBins - 1; i++)
            {
                // bin i and everything to the left will be the left node
                // everything to the right of i will be the right node
                BBox leftBB = BBox.EmptyBBox(), rightBB = BBox.EmptyBBox();
                uint leftTriCount = 0, rightTriCount = 0;

                // construct right bb
                for (int j = i+1; j < numBins; j++) {
                    rightTriCount += binCounts[j];
                    rightBB = rightBB.Union(binBBs[j] ?? BBox.EmptyBBox());
                }

                // construct left bb
                    // TODO: optimize. can just accumulate this in O(1) time
                for (int k = 0; k <= i; k++) {
                    leftTriCount += binCounts[k];
                    leftBB = leftBB.Union(binBBs[k] ?? BBox.EmptyBBox());
                }

                float partitionCost = (leftTriCount * leftBB.SA()) + (rightTriCount * rightBB.SA());
                
                // update min cost
                if (partitionCost < minCost) {
                    minCost = partitionCost;
                    bestAxis = axis;
                    bestSplitPos = bbAxisStart + (i+1)*stepSize;
                }
            }
        }
        
        if (bestAxis == Axis.NONE) // edge case where no split improves cost
            return BVH_Node.BuildLeafNode(tris);

        // partition triangles
        var leftTris = new List<Triangle>();
        var rightTris = new List<Triangle>();
        foreach (var tri in tris)
        {
            if (tri.bb.getCenter(bestAxis) < bestSplitPos)
                leftTris.Add(tri);
            else 
                rightTris.Add(tri);
        }
        // partition did not split triangles
        if (leftTris.Count == tris.Count || rightTris.Count == tris.Count) {
            Debug.Log("after split, all triangles put in one side, depth: " + depth + "count: " + tris.Count);
            return BVH_Node.BuildLeafNode(tris);
        }

        // initialize current node params
        BVH_Node innerNode = new BVH_Node();
        innerNode.leaf = false;
        innerNode.bb = trisBB;
        // TODO: make this recursive construction multithreaded?
        innerNode.left = BuildBVHNode(leftTris, depth + 1);
        innerNode.right = BuildBVHNode(rightTris, depth + 1);
        // set child parent
        innerNode.left.parent = innerNode;
        innerNode.right.parent = innerNode;

        return innerNode;
    }

    public static BVH_Node BuildLeafNode(List<Triangle> tris) {
        BVH_Node bvh = new BVH_Node();
        bvh.leaf = true;
        bvh.bb = BBox.BBoxFromTris(tris);
        bvh.triangles = tris;
        return bvh;
    }

    public void Draw() {
        this.bb.Draw(Color.yellow);
        if (this.left != null)
            this.left.bb.Draw(Color.black); 
        if (this.right != null)
            this.right.bb.Draw(Color.black);
        
        if (this.leaf) {
            Debug.Log("leaf node contains " + this.triangles.Count + " tris");
        }

    }

    public int Depth() {
        if (this.leaf)
            return 1;

        int leftDepth = 0, rightDepth = 0;
        if (this.left != null)
            leftDepth = this.left.Depth();
        if (this.right != null)
            rightDepth = this.right.Depth();
        
        return 1 + Mathf.Max(leftDepth, rightDepth);
    }

    public int Size() {
        if (this.leaf)
            return 1;
        int leftSize = 0, rightSize = 0;
        if (this.left != null)
            leftSize = this.left.Size();
        if (this.right != null) 
            rightSize = this.right.Size();
        return 1 + leftSize + rightSize;
    }

}
public enum Axis {
    X, Y, Z, NONE
}


public struct GPU_BVH_Node {  // optimized for BVH Traversal on the GPU
    public Vector3 bot, top; // BBox
    public int leftOrCount;  // if leaf, number of tri indices. Else index of left child
    public int rightOrOffset; // if leaf, offset into indices buffer. Else index of right child
    public int isLeaf;
}