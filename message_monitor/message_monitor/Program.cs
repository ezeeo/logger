using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace message_monitor
{
    class Program
    {
        private string console_ram_name;
        private MemoryMappedFile console_message_ram;//保存消息
        private MemoryMappedFile console_position_ram;//保存标志位
        private MemoryMappedViewStream console_stream;//读写消息的流
        private MemoryMappedViewAccessor console_access;//读写标志
        private char have_data;//标志有无数据，t，f
        private char rwf_falg;//标志内存状态，r（读取中），w（写入中），f（空闲）
        private char control;//控制log显示器进程运行状态，r（运行），p（暂停），s（停止但不退出），c（关闭程序）
        BinaryReader b_reader;//流的读写器


        static void Main(string[] args)
        {
            if(args.Length!=1)
            {
                Console.WriteLine("args.lenth="+args.Length);
                Console.ReadKey();
                return;
            }
            
            Program program = new Program();
            program.console_ram_name = args[0];
            try
            {
                program.init_console();
            }
            catch (Exception ex)
            {
                Console.WriteLine("error! cannot init console");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.ReadKey();
                return;
            }
            

            Console.WriteLine("----------------get ram success----------------");

            int error_num = 0;
            Thread.Sleep(0);//确保打开锁
            Mutex mutex;

            try
            {
                mutex = Mutex.OpenExisting(program.console_ram_name.Substring(8, 8));
            }
            catch (Exception ex)
            {
                Console.WriteLine("error! cannot open mutex");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.ReadKey();
                return;
            }
            

            Console.WriteLine("---------------open mutex success--------------");

            Console.WriteLine("------------------now monitor------------------");
            int no_data_time = 0;

            bool rewait = false;
            while (true)
            {
                rewait = false;
                //Thread.Sleep(0);
                //检查标志位
                mutex.WaitOne();//等待写入完成
                

                if (program.console_access.CanRead == true)
                {
                    error_num = 0;

                    program.have_data = program.console_access.ReadChar(0);
                    program.rwf_falg = program.console_access.ReadChar(2);
                    program.control = program.console_access.ReadChar(4);

                    //Console.WriteLine("have_data="+ program.have_data+" rwf_flag="+ program.rwf_falg+" control="+ program.control);

                    switch (program.control)
                    {
                        case 'c':
                            mutex.ReleaseMutex();
                            return;
                        case 's':
                            mutex.ReleaseMutex();
                            Console.WriteLine("------------------monitor stop success press any key to exit------------------");
                            Console.ReadKey();
                            return;
                        case 'p':
                            Console.WriteLine("------------------monitor pause------------------");
                            mutex.ReleaseMutex();
                            while (true)
                            {
                                Thread.Sleep(1);
                                mutex.WaitOne();
                                program.control = program.console_access.ReadChar(4);
                                mutex.ReleaseMutex();
                                if (program.control!='p')
                                {
                                    break;
                                }
                                else
                                {
                                    Thread.Sleep(30);
                                }
                            }
                            Console.WriteLine("------------------monitor run------------------");
                            rewait = true;
                            break;
                        case 'r':
                            break;
                        default:
                            Console.WriteLine("error:program.control="+ program.control);
                            break;
                            Console.ReadKey();
                            throw new Exception("error value in read control:control==default");
                    }
                    if (rewait == true) continue;

                    switch (program.rwf_falg)
                    {
                        case 'r':
                            Console.WriteLine("error:program.rwf_falg=" + program.rwf_falg);
                            mutex.ReleaseMutex();
                            continue;
                            Console.ReadKey();
                            throw new Exception("error value in read rwf_falg:rwf_falg=='r'");
                        case 'w':
                            mutex.ReleaseMutex();
                            Thread.Sleep(0);
                            continue;
                        case 'f':
                            if (program.have_data=='t')
                            {
                                //Console.WriteLine("have data");

                                program.console_access.Write(2, 'r');

                                program.b_reader.BaseStream.Seek(0, SeekOrigin.Begin);
                                Console.WriteLine(program.b_reader.ReadString());
                                program.console_access.Write(0, 'f');//设为无数据
                                program.console_access.Write(2, 'f');//设为空闲
                                mutex.ReleaseMutex();
                            }
                            else
                            {
                                mutex.ReleaseMutex();
                                no_data_time++;
                            }
                            if (no_data_time>5)
                            {
                                no_data_time = 0;
                                Thread.Sleep(1);
                            }
                            else
                            {
                                Thread.Sleep(0);
                            }
                            break;
                        default:
                            Console.WriteLine("error:program.rwf_falg=" + program.rwf_falg);
                            mutex.ReleaseMutex();
                            continue;
                            Console.ReadKey();
                            throw new Exception("error value in read rwf_falg:rwf_falg==default");
                    }
                }
                else
                {
                    mutex.ReleaseMutex();
                    error_num++;
                    if (error_num>=3)
                    {
                        Console.WriteLine("error in read:console_access can not read");
                        error_num = 0;
                        continue;
                        Console.ReadKey();
                        throw new Exception("error in read:console_access can not read");
                    }
                    Thread.Sleep(1);
                }
            }
        }





        private void init_console()
        {
            //初始化消息空间
            if (console_ram_name == null || console_ram_name == string.Empty)
            {
                Console.ReadKey();
                throw new Exception("error in init:console_ram_name==null");
            }
            if (console_message_ram == null)
            {
                //创建共享内存，大小1K
                console_message_ram = MemoryMappedFile.OpenExisting(console_ram_name);
            }
            if (console_stream == null)
            {
                //创建写入流
                console_stream = console_message_ram.CreateViewStream();
            }
            if (b_reader == null)
            {
                b_reader = new BinaryReader(console_stream);
            }
            //初始化标志空间
            if (console_position_ram == null)
            {
                //创建标志位内存，大小16字节
                console_position_ram = MemoryMappedFile.OpenExisting(console_ram_name.Substring(0,8));
            }
            if (console_access == null)
            {
                console_access = console_position_ram.CreateViewAccessor();
            }
        }
    }
}
