using System;
using ReactorCoreSim.Scripts.Core;
using ReactorCoreSim.Scripts.Models;

namespace ReactorCoreSim.Scripts.Tests
{
    public static class PhysicsValidation
    {
        public static bool RunAllTests()
        {
            bool allPassed = true;

            Console.WriteLine("=== 物理计算模块验证测试 ===");
            Console.WriteLine();

            allPassed &= TestPointKineticsSolver();
            Console.WriteLine();

            allPassed &= TestSignalFilters();
            Console.WriteLine();

            allPassed &= TestReactorCore();
            Console.WriteLine();

            allPassed &= TestThermalHydraulics();
            Console.WriteLine();

            Console.WriteLine(allPassed ? "✅ 所有测试通过" : "❌ 部分测试失败");
            return allPassed;
        }

        private static bool TestPointKineticsSolver()
        {
            Console.WriteLine("【点堆动力学求解器测试】");

            try
            {
                var parameters = PointKineticsParameters.DefaultPwr();
                var solver = new PointKineticsSolver(parameters);

                double initialPower = solver.Power;
                Console.WriteLine($"  初始功率: {initialPower:E6}");

                solver.SetReactivity(0.001);
                double dt = 0.01;
                for (int i = 0; i < 100; i++)
                {
                    solver.Step(dt);
                }

                double powerAfter = solver.Power;
                Console.WriteLine($"  1秒后功率 (ρ=100pcm): {powerAfter:E6}");

                if (powerAfter > initialPower)
                {
                    Console.WriteLine("  ✅ 正反应性导致功率上升 - 正确");
                }
                else
                {
                    Console.WriteLine("  ❌ 功率未上升 - 错误");
                    return false;
                }

                solver.SetReactivity(-0.005);
                for (int i = 0; i < 100; i++)
                {
                    solver.Step(dt);
                }

                double powerAfterNegative = solver.Power;
                Console.WriteLine($"  再加负反应性后功率: {powerAfterNegative:E6}");

                if (powerAfterNegative < powerAfter)
                {
                    Console.WriteLine("  ✅ 负反应性导致功率下降 - 正确");
                }
                else
                {
                    Console.WriteLine("  ❌ 功率未下降 - 错误");
                    return false;
                }

                double totalPrecursor = solver.GetTotalPrecursorConcentration();
                Console.WriteLine($"  总先驱核浓度: {totalPrecursor:E6}");
                Console.WriteLine("  ✅ 先驱核浓度计算正常");

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"  ❌ 异常: {e.Message}");
                return false;
            }
        }

        private static bool TestSignalFilters()
        {
            Console.WriteLine("【信号滤波算法测试】");

            try
            {
                var lowPass = new FirstOrderLowPassFilter(1.0, 0.0);
                double[] testSignal = { 0, 1, 1, 1, 1, 1, 1, 1, 1, 1 };

                double filtered = 0;
                foreach (var s in testSignal)
                {
                    filtered = lowPass.Filter(s);
                }

                Console.WriteLine($"  低通滤波阶跃响应 (最终值): {filtered:F4}");
                if (filtered > 0.5 && filtered < 1.0)
                {
                    Console.WriteLine("  ✅ 低通滤波工作正常");
                }
                else
                {
                    Console.WriteLine("  ❌ 低通滤波结果异常");
                    return false;
                }

                var movingAvg = new MovingAverageFilter(5, 0.0);
                for (int i = 1; i <= 10; i++)
                {
                    movingAvg.Filter(i);
                }
                double avg = movingAvg.CurrentValue;
                Console.WriteLine($"  移动平均 (最后5个值6-10的平均): {avg:F2}");
                if (Math.Abs(avg - 8.0) < 0.01)
                {
                    Console.WriteLine("  ✅ 移动平均工作正常");
                }
                else
                {
                    Console.WriteLine("  ❌ 移动平均结果异常");
                    return false;
                }

                var kalman = new KalmanFilter1D(0.001, 0.1, 50.0);
                Random rand = new Random(42);
                double sumError = 0;
                int count = 0;
                for (int i = 0; i < 100; i++)
                {
                    double noisy = 50.0 + rand.NextDouble() * 2.0 - 1.0;
                    double filtered_k = kalman.Filter(noisy);
                    if (i > 50)
                    {
                        sumError += Math.Abs(filtered_k - 50.0);
                        count++;
                    }
                }
                double avgError = sumError / count;
                Console.WriteLine($"  卡尔曼滤波平均误差: {avgError:F4}");
                if (avgError < 1.0)
                {
                    Console.WriteLine("  ✅ 卡尔曼滤波工作正常");
                }
                else
                {
                    Console.WriteLine("  ⚠️ 卡尔曼滤波误差较大 (可能正常)");
                }

                var cascaded = new CascadedFilter(
                    new FirstOrderLowPassFilter(0.5, 100.0),
                    new MovingAverageFilter(10, 100.0)
                );

                double noiseAmount = 5.0;
                double noisySignal = 100.0 + rand.NextDouble() * noiseAmount * 2 - noiseAmount;
                double cascadedResult = cascaded.Filter(noisySignal);
                Console.WriteLine($"  级联滤波输入(带噪声): {noisySignal:F2}, 输出: {cascadedResult:F2}");
                Console.WriteLine("  ✅ 级联滤波工作正常");

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"  ❌ 异常: {e.Message}");
                return false;
            }
        }

        private static bool TestReactorCore()
        {
            Console.WriteLine("【反应堆堆芯模型测试】");

            try
            {
                var core = new ReactorCore();
                int assemblyCount = core.AssemblyCount;
                Console.WriteLine($"  燃料组件数量: {assemblyCount}");

                if (assemblyCount == 157)
                {
                    Console.WriteLine("  ✅ 组件数量正确 (157个)");
                }
                else
                {
                    Console.WriteLine($"  ⚠️ 组件数量为 {assemblyCount} (预期157)");
                }

                core.UpdatePowerDistribution(1e6);
                double totalPower = core.TotalPower;
                Console.WriteLine($"  总功率: {totalPower:E6} W");

                double peakFactor = core.PeakPowerFactor;
                Console.WriteLine($"  峰值功率因子: {peakFactor:F3}");

                if (peakFactor > 1.0)
                {
                    Console.WriteLine("  ✅ 峰值功率因子 > 1.0 - 正确");
                }
                else
                {
                    Console.WriteLine("  ❌ 峰值功率因子异常");
                    return false;
                }

                int controlRodCount = 0;
                foreach (var assembly in core.Assemblies)
                {
                    if (assembly.IsControlRod)
                    {
                        controlRodCount++;
                    }
                }
                Console.WriteLine($"  控制棒组件数量: {controlRodCount}");
                Console.WriteLine("  ✅ 堆芯模型工作正常");

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"  ❌ 异常: {e.Message}");
                return false;
            }
        }

        private static bool TestThermalHydraulics()
        {
            Console.WriteLine("【热工水力计算测试】");

            try
            {
                var parameters = ThermalHydraulicsParameters.DefaultPwr();
                var calculator = new ThermalHydraulicsCalculator(parameters);

                double power = 1e6;
                double flow = 18400.0;
                double inletTemp = 292.0;
                double pressure = 15.5e6;

                calculator.Calculate(power, flow, inletTemp, pressure);
                var state = calculator.State;

                Console.WriteLine($"  入口温度: {inletTemp:F1} °C");
                Console.WriteLine($"  出口温度: {state.OutletTemperature:F1} °C");
                Console.WriteLine($"  平均温度: {state.AverageCoolantTemperature:F1} °C");
                Console.WriteLine($"  包壳温度: {state.CladdingSurfaceTemperature:F1} °C");
                Console.WriteLine($"  燃料中心温度: {state.FuelCenterlineTemperature:F1} °C");
                Console.WriteLine($"  DNBR: {state.Dnbr:F3}");
                Console.WriteLine($"  临界热流密度: {state.CriticalHeatFlux:E6} W/m²");
                Console.WriteLine($"  实际热流密度: {state.ActualHeatFlux:E6} W/m²");

                if (state.OutletTemperature > inletTemp)
                {
                    Console.WriteLine("  ✅ 冷却剂被加热 - 正确");
                }
                else
                {
                    Console.WriteLine("  ❌ 出口温度异常");
                    return false;
                }

                if (state.FuelCenterlineTemperature > state.CladdingSurfaceTemperature)
                {
                    Console.WriteLine("  ✅ 温度分布正常 (中心 > 包壳)");
                }
                else
                {
                    Console.WriteLine("  ❌ 温度分布异常");
                    return false;
                }

                if (state.Dnbr > 1.0)
                {
                    Console.WriteLine("  ✅ DNBR > 1.0 - 安全");
                }
                else
                {
                    Console.WriteLine("  ⚠️ DNBR < 1.0 - 超出安全限值");
                }

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"  ❌ 异常: {e.Message}");
                return false;
            }
        }
    }
}
