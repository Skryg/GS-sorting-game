// SPDX-License-Identifier: MIT
using System;
using System.Threading.Tasks;
using Unity.Mathematics;

namespace GaussianSplatting.Editor.Utils
{
    // Reduces the splat count by repeatedly joining pairs of neighbours that look alike.
    // A pair becomes one Gaussian with the same mean and covariance. Optical depth times area is preserved,
    // so splats lying on top of each other give exactly 1-(1-a)(1-b).
    public static class GaussianSplatMerger
    {
        const int kNeighbours = 10;
        const double kMergedPerRound = 0.15;
        const float kMaxOpacity = 0.9995f;
        const double kColorWeight = 30.0;
        const double kSHWeight = 0.08;
        const int kComparedSH = 8; // bands 1 and 2
        const int kBatch = 2048;

        // The progress callback gets 0..1 and returns false to cancel, which makes the result null.
        public static GaussianSplatCloud Merge(GaussianSplatCloud source, int target, Func<float, bool> progress = null)
        {
            target = math.max(1, target);
            var cloud = Copy(source);
            int startCount = cloud.count;
            if (startCount <= target)
                return cloud;

            double epsilon = FlatnessEpsilon(cloud);
            var area = new double[startCount];
            var mass = new double[startCount];
            var logDet = new double[startCount];
            var best = new int[startCount];
            var cost = new double[startCount];
            var order = new int[startCount];
            var sortKeys = new double[startCount];
            var tree = new KdTree(startCount);

            while (cloud.count > target)
            {
                int count = cloud.count;
                if (progress != null && !progress((float)(startCount - count) / (startCount - target)))
                    return null;

                tree.Build(cloud.pos, count);
                ForBatches(count, (begin, end) =>
                {
                    for (int i = begin; i < end; i++)
                    {
                        area[i] = cloud.cov[i].Area();
                        mass[i] = OpticalDepth(cloud.opacity[i]) * area[i];
                        logDet[i] = cloud.cov[i].LogDet(epsilon);
                    }
                });
                ForBatches(count, (begin, end) =>
                {
                    var found = new int[kNeighbours];
                    var distances = new double[kNeighbours];
                    for (int i = begin; i < end; i++)
                    {
                        best[i] = -1;
                        cost[i] = double.PositiveInfinity;
                        int n = tree.FindNearest(i, found, distances);
                        for (int k = 0; k < n; k++)
                        {
                            double c = Cost(cloud, area, mass, logDet, epsilon, i, found[k]);
                            if (c < cost[i])
                            {
                                cost[i] = c;
                                best[i] = found[k];
                            }
                        }
                    }
                });

                for (int i = 0; i < count; i++)
                {
                    order[i] = i;
                    sortKeys[i] = cost[i];
                }
                Array.Sort(sortKeys, order, 0, count);

                // every splat takes part in at most one merge per round
                int limit = math.min(count - target, (int)math.ceil(count * kMergedPerRound));
                var used = new bool[count];
                var removed = new bool[count];
                int merged = 0;
                for (int n = 0; n < count && merged < limit; n++)
                {
                    int i = order[n], j = best[i];
                    if (j < 0 || used[i] || used[j])
                        continue;
                    used[i] = used[j] = true;
                    removed[j] = true;
                    Join(cloud, area, mass, i, j);
                    merged++;
                }
                if (merged == 0)
                    break;
                Compact(cloud, removed);
            }
            return cloud;
        }

        static double OpticalDepth(float opacity) => -math.log(1.0 - math.min(opacity, kMaxOpacity));

        static double Cost(GaussianSplatCloud cloud, double[] area, double[] mass, double[] logDet, double epsilon, int i, int j)
        {
            double wi = Weight(mass, i, j), wj = 1.0 - wi;
            var joined = JoinCovariance(cloud, i, j, wi);
            double shape = 0.5 * (joined.LogDet(epsilon) - wi * logDet[i] - wj * logDet[j]);

            double colorDiff = math.lengthsq(cloud.color[i] - cloud.color[j]);
            double shDiff = 0;
            for (int k = 0; k < kComparedSH; k++)
                shDiff += math.lengthsq(cloud.sh[i * GaussianSplatCloud.kSHCount + k] - cloud.sh[j * GaussianSplatCloud.kSHCount + k]);

            // scaled by how much the pair can change on screen
            double visible = math.min(cloud.opacity[i] * area[i], cloud.opacity[j] * area[j]);
            return visible * (math.max(0.0, shape) + kColorWeight * (colorDiff + kSHWeight * shDiff));
        }

        static double Weight(double[] mass, int i, int j)
        {
            double sum = mass[i] + mass[j];
            return sum > 0 ? mass[i] / sum : 0.5;
        }

        static SplatCovariance JoinCovariance(GaussianSplatCloud cloud, int i, int j, double wi)
        {
            double wj = 1.0 - wi, w = wi * wj;
            double3 d = cloud.pos[j] - cloud.pos[i];
            ref readonly var a = ref cloud.cov[i];
            ref readonly var b = ref cloud.cov[j];
            return new SplatCovariance
            {
                xx = wi * a.xx + wj * b.xx + w * d.x * d.x,
                xy = wi * a.xy + wj * b.xy + w * d.x * d.y,
                xz = wi * a.xz + wj * b.xz + w * d.x * d.z,
                yy = wi * a.yy + wj * b.yy + w * d.y * d.y,
                yz = wi * a.yz + wj * b.yz + w * d.y * d.z,
                zz = wi * a.zz + wj * b.zz + w * d.z * d.z
            };
        }

        static void Join(GaussianSplatCloud cloud, double[] area, double[] mass, int i, int j)
        {
            double wi = Weight(mass, i, j);
            var cov = JoinCovariance(cloud, i, j, wi);

            // colors are weighted by what is visible, optical depth would favour opaque splats too much
            double vi = cloud.opacity[i] * area[i], vj = cloud.opacity[j] * area[j];
            float t = vi + vj > 0 ? (float)(vj / (vi + vj)) : 0.5f;
            cloud.color[i] = math.lerp(cloud.color[i], cloud.color[j], t);
            for (int k = 0; k < GaussianSplatCloud.kSHCount; k++)
            {
                int si = i * GaussianSplatCloud.kSHCount + k, sj = j * GaussianSplatCloud.kSHCount + k;
                cloud.sh[si] = math.lerp(cloud.sh[si], cloud.sh[sj], t);
            }

            cloud.pos[i] = wi * cloud.pos[i] + (1.0 - wi) * cloud.pos[j];
            cloud.cov[i] = cov;
            cloud.opacity[i] = (float)(1.0 - math.exp(-(mass[i] + mass[j]) / cov.Area()));
        }

        // Keeps flat splats from having a zero determinant; relative to the typical splat size.
        static double FlatnessEpsilon(GaussianSplatCloud cloud)
        {
            var variances = new double[cloud.count];
            for (int i = 0; i < cloud.count; i++)
                variances[i] = math.max(cloud.cov[i].xx, math.max(cloud.cov[i].yy, cloud.cov[i].zz));
            Array.Sort(variances);
            return math.max(variances[cloud.count / 2] * 1e-3, 1e-18);
        }

        static void ForBatches(int count, Action<int, int> action)
        {
            Parallel.For(0, (count + kBatch - 1) / kBatch, b => action(b * kBatch, math.min((b + 1) * kBatch, count)));
        }

        static GaussianSplatCloud Copy(GaussianSplatCloud source)
        {
            var copy = new GaussianSplatCloud(source.count);
            Array.Copy(source.pos, copy.pos, source.count);
            Array.Copy(source.cov, copy.cov, source.count);
            Array.Copy(source.opacity, copy.opacity, source.count);
            Array.Copy(source.color, copy.color, source.count);
            Array.Copy(source.sh, copy.sh, source.count * GaussianSplatCloud.kSHCount);
            return copy;
        }

        static void Compact(GaussianSplatCloud cloud, bool[] removed)
        {
            int w = 0;
            for (int i = 0; i < cloud.count; i++)
            {
                if (removed[i])
                    continue;
                if (w != i)
                {
                    cloud.pos[w] = cloud.pos[i];
                    cloud.cov[w] = cloud.cov[i];
                    cloud.opacity[w] = cloud.opacity[i];
                    cloud.color[w] = cloud.color[i];
                    Array.Copy(cloud.sh, i * GaussianSplatCloud.kSHCount, cloud.sh, w * GaussianSplatCloud.kSHCount, GaussianSplatCloud.kSHCount);
                }
                w++;
            }
            cloud.count = w;
        }

        // Balanced tree stored implicitly: the median of every index range is its root.
        class KdTree
        {
            const int kLeafSize = 8;
            readonly int[] m_Items;
            readonly byte[] m_Axis;
            double3[] m_Pos;
            int m_Count;

            public KdTree(int capacity)
            {
                m_Items = new int[capacity];
                m_Axis = new byte[capacity];
            }

            public void Build(double3[] positions, int count)
            {
                m_Pos = positions;
                m_Count = count;
                for (int i = 0; i < count; i++)
                    m_Items[i] = i;
                Split(0, count, 0);
            }

            void Split(int begin, int end, int depth)
            {
                if (end - begin <= kLeafSize)
                    return;

                double3 min = double.PositiveInfinity, max = double.NegativeInfinity;
                for (int i = begin; i < end; i++)
                {
                    min = math.min(min, m_Pos[m_Items[i]]);
                    max = math.max(max, m_Pos[m_Items[i]]);
                }
                double3 size = max - min;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;

                int mid = (begin + end) / 2;
                Select(begin, end - 1, mid, axis);
                m_Axis[mid] = (byte)axis;

                if (depth < 4)
                    Parallel.Invoke(() => Split(begin, mid, depth + 1), () => Split(mid + 1, end, depth + 1));
                else
                {
                    Split(begin, mid, depth + 1);
                    Split(mid + 1, end, depth + 1);
                }
            }

            void Select(int left, int right, int k, int axis)
            {
                while (left < right)
                {
                    double pivot = m_Pos[m_Items[(left + right) / 2]][axis];
                    int i = left, j = right;
                    while (i <= j)
                    {
                        while (m_Pos[m_Items[i]][axis] < pivot) i++;
                        while (m_Pos[m_Items[j]][axis] > pivot) j--;
                        if (i > j)
                            break;
                        (m_Items[i], m_Items[j]) = (m_Items[j], m_Items[i]);
                        i++;
                        j--;
                    }
                    if (k <= j) right = j;
                    else if (k >= i) left = i;
                    else return;
                }
            }

            // Fills both arrays sorted by distance, without the item itself.
            public int FindNearest(int item, int[] found, double[] distances)
            {
                int n = 0;
                Search(0, m_Count, item, m_Pos[item], found, distances, ref n);
                return n;
            }

            void Search(int begin, int end, int item, in double3 p, int[] found, double[] distances, ref int n)
            {
                if (end - begin <= kLeafSize)
                {
                    for (int i = begin; i < end; i++)
                        Add(m_Items[i], item, p, found, distances, ref n);
                    return;
                }

                int mid = (begin + end) / 2;
                int axis = m_Axis[mid];
                double d = p[axis] - m_Pos[m_Items[mid]][axis];
                if (d < 0) Search(begin, mid, item, p, found, distances, ref n);
                else Search(mid + 1, end, item, p, found, distances, ref n);

                if (n == found.Length && d * d >= distances[n - 1])
                    return;
                Add(m_Items[mid], item, p, found, distances, ref n);
                if (d < 0) Search(mid + 1, end, item, p, found, distances, ref n);
                else Search(begin, mid, item, p, found, distances, ref n);
            }

            void Add(int candidate, int item, in double3 p, int[] found, double[] distances, ref int n)
            {
                if (candidate == item)
                    return;
                double d = math.lengthsq(m_Pos[candidate] - p);
                if (n == found.Length)
                {
                    if (d >= distances[n - 1])
                        return;
                    n--;
                }
                int slot = n++;
                for (; slot > 0 && distances[slot - 1] > d; slot--)
                {
                    distances[slot] = distances[slot - 1];
                    found[slot] = found[slot - 1];
                }
                distances[slot] = d;
                found[slot] = candidate;
            }
        }
    }
}
