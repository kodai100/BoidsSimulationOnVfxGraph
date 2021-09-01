using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BoidsSimulationOnGPU
{
    public class GPUBoids : MonoBehaviour
    {
        // スレッドグループのスレッドのサイズ
        const int SIMULATION_BLOCK_SIZE = 256;

        #region Boids Parameters

        // 最大オブジェクト数
        [Range(256, 32768)] public int MaxObjectNum = 16384;

        // 結合を適用する他の個体との半径
        public float CohesionNeighborhoodRadius = 2.0f;

        // 整列を適用する他の個体との半径
        public float AlignmentNeighborhoodRadius = 2.0f;

        // 分離を適用する他の個体との半径
        public float SeparateNeighborhoodRadius = 1.0f;

        // 速度の最大値
        public float MaxSpeed = 5.0f;

        // 操舵力の最大値
        public float MaxSteerForce = 0.5f;

        // 結合する力の重み
        public float CohesionWeight = 1.0f;

        // 整列する力の重み
        public float AlignmentWeight = 1.0f;

        // 分離する力の重み
        public float SeparateWeight = 3.0f;

        // 壁を避ける力の重み
        public float AvoidWallWeight = 10.0f;

        // 壁の中心座標   
        //public Vector3 WallCenter = Vector3.zero;

        // 壁のサイズ
        public Vector3 WallSize = new Vector3(32.0f, 32.0f, 32.0f);

        #endregion

//

        #region Built-in Resources

        // Boidsシミュレーションを行うComputeShaderの参照
        public ComputeShader BoidsCS;

        #endregion

        #region Private Resources

        // Boidの操舵力（Force）を格納したバッファ
        GraphicsBuffer _boidForceBuffer;

        // Boidの基本データ（速度, 位置, Transformなど）を格納したバッファ
        private GraphicsBuffer _boidPositionBuffer;
        GraphicsBuffer _boidVelocityBuffer;

        #endregion

        #region Accessors

        // Boidの基本データを格納したバッファを取得
        public GraphicsBuffer GetBoidDataBuffer()
        {
            return this._boidPositionBuffer != null ? this._boidPositionBuffer : null;
        }

        // オブジェクト数を取得
        public int GetMaxObjectNum()
        {
            return this.MaxObjectNum;
        }

        // シミュレーション領域の中心座標を返す
        public Vector3 GetSimulationAreaCenter()
        {
            return this.transform.position;
        }

        // シミュレーション領域のボックスのサイズを返す
        public Vector3 GetSimulationAreaSize()
        {
            return this.WallSize;
        }

        #endregion

        #region MonoBehaviour Functions

        void Start()
        {
            // バッファを初期化
            InitBuffer();
        }

        void Update()
        {
            // シミュレーション
            Simulation();
        }

        void OnDestroy()
        {
            // バッファを破棄
            ReleaseBuffer();
        }

        void OnDrawGizmos()
        {
            // デバッグとしてシミュレーション領域をワイヤーフレームで描画
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, WallSize);
        }

        #endregion

        #region Private Functions

        // バッファを初期化
        void InitBuffer()
        {
            // バッファを初期化
            _boidPositionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxObjectNum,
                Marshal.SizeOf(typeof(Vector3)));

            _boidVelocityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxObjectNum,
                Marshal.SizeOf(typeof(Vector3)));


            _boidForceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxObjectNum,
                Marshal.SizeOf(typeof(Vector3)));

            // Boidデータ, Forceバッファを初期化
            var forceArr = new Vector3[MaxObjectNum];
            var boidPositionDataArr = new Vector3[MaxObjectNum];
            var boidVelocityDataArr = new Vector3[MaxObjectNum];
            for (var i = 0; i < MaxObjectNum; i++)
            {
                forceArr[i] = Vector3.zero;
                boidPositionDataArr[i] = transform.position + Random.insideUnitSphere * 1.0f;
                boidVelocityDataArr[i] = Random.insideUnitSphere * 0.1f;
            }

            _boidForceBuffer.SetData(forceArr);

            _boidPositionBuffer.SetData(boidPositionDataArr);
            _boidVelocityBuffer.SetData(boidVelocityDataArr);
            forceArr = null;
            boidPositionDataArr = null;
            boidVelocityDataArr = null;
        }

        // シミュレーション
        void Simulation()
        {
            ComputeShader cs = BoidsCS;
            int id = -1;

            // スレッドグループの数を求める
            int threadGroupSize = Mathf.CeilToInt(MaxObjectNum / SIMULATION_BLOCK_SIZE);

            // 操舵力を計算
            id = cs.FindKernel("ForceCS"); // カーネルIDを取得
            cs.SetInt("_MaxBoidObjectNum", MaxObjectNum);
            cs.SetFloat("_CohesionNeighborhoodRadius", CohesionNeighborhoodRadius);
            cs.SetFloat("_AlignmentNeighborhoodRadius", AlignmentNeighborhoodRadius);
            cs.SetFloat("_SeparateNeighborhoodRadius", SeparateNeighborhoodRadius);
            cs.SetFloat("_MaxSpeed", MaxSpeed);
            cs.SetFloat("_MaxSteerForce", MaxSteerForce);
            cs.SetFloat("_SeparateWeight", SeparateWeight);
            cs.SetFloat("_CohesionWeight", CohesionWeight);
            cs.SetFloat("_AlignmentWeight", AlignmentWeight);
            cs.SetVector("_WallCenter", transform.position);
            cs.SetVector("_WallSize", WallSize);
            cs.SetFloat("_AvoidWallWeight", AvoidWallWeight);
            cs.SetBuffer(id, "_BoidPositionDataBufferRead", _boidPositionBuffer);
            cs.SetBuffer(id, "_BoidVelocityDataBufferRead", _boidVelocityBuffer);
            cs.SetBuffer(id, "_BoidForceBufferWrite", _boidForceBuffer);
            cs.Dispatch(id, threadGroupSize, 1, 1); // ComputeShaderを実行

            // 操舵力から、速度と位置を計算
            id = cs.FindKernel("IntegrateCS"); // カーネルIDを取得
            cs.SetFloat("_DeltaTime", Time.deltaTime);
            cs.SetBuffer(id, "_BoidForceBufferRead", _boidForceBuffer);
            cs.SetBuffer(id, "_BoidPositionDataBufferWrite", _boidPositionBuffer);
            cs.SetBuffer(id, "_BoidVelocityDataBufferWrite", _boidVelocityBuffer);
            cs.Dispatch(id, threadGroupSize, 1, 1); // ComputeShaderを実行
        }

        // バッファを解放
        void ReleaseBuffer()
        {
            if (_boidPositionBuffer != null)
            {
                _boidPositionBuffer.Release();
                _boidPositionBuffer = null;
            }

            if (_boidVelocityBuffer != null)
            {
                _boidVelocityBuffer.Release();
                _boidVelocityBuffer = null;
            }

            if (_boidForceBuffer != null)
            {
                _boidForceBuffer.Release();
                _boidForceBuffer = null;
            }
        }

        #endregion
    } // class
} // namespace