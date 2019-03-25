using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace External_library
{
    //输出模式（控制台，文件，内存）
    //输出路径
    //内部缓存最大值（内存占用/条数）
    //文件最大值
    //文件名，每一个logger对应一个文件，可以多个logger对于一个文件，尝试写方法为静态
    //设置独立的‘写’线程
    //支持使用多线程写文件
    //日志等级

    public class Logger
    {
        private bool pri = false;

        public enum Level { none,error,warning,info,debug}

        private Level log_level=Level.debug;

        Writter.Work_type type = Writter.Work_type.console;

        Writter writter;

        internal Level Log_level { get => log_level; set => log_level = value; }

        public Logger(Writter.Work_type type)
        {
            this.type = type;
            writter = Writter.get_writter();
        }
        public Logger(Writter.Work_type type,string file_path_or_name)
        {
            this.type = type;
            writter = Writter.get_writter();
            switch (type)
            {
                case Writter.Work_type.file:
                    writter.File_path = file_path_or_name;
                    break;
                case Writter.Work_type.console:
                    break;
                case Writter.Work_type.ram:
                    writter.Ram_file_name = file_path_or_name;
                    writter.Memory_capacity = 1024;
                    break;
                default:
                    break;
            }
        }
        public Logger(Writter.Work_type type, string file_path_or_name, bool init_now)
        {
            this.type = type;
            writter = Writter.get_writter();
            switch (type)
            {
                case Writter.Work_type.file:
                    writter.File_path = file_path_or_name;
                    if (init_now)
                    {
                        writter.init_file();
                    }
                    break;
                case Writter.Work_type.console:
                    break;
                case Writter.Work_type.ram:
                    writter.Ram_file_name = file_path_or_name;
                    writter.Memory_capacity = 1024;
                    if (init_now)
                    {
                        writter.init_memory_file();
                    }
                    break;
                default:
                    break;
            }
            
        }
        public Logger(Writter.Work_type type,bool init_now)
        {
            this.type = type;
            writter = Writter.get_writter();
            if (init_now)
            {
                switch (type)
                {
                    case Writter.Work_type.file:
                        throw new Exception("must set file path");
                        break;
                    case Writter.Work_type.console:
                        writter.init_console();
                        break;
                    case Writter.Work_type.ram:
                        throw new Exception("must set memory_file name");
                        break;
                    default:
                        break;
                }
                
            }

        }

        public void set_file_path(string path)
        {
            writter.File_path = path;
        }

        public void set_ram_name(string name)
        {
            writter.Ram_file_name = name;
        }

        public string get_ram_name()
        {
            return writter.Ram_file_name;
        }
        /// <summary>
        /// 用于在close_monitor后需要重新拉起日志控制台，其他时候无效
        /// </summary>
        public void reinit_monitor()
        {
            writter.allow_start_monitor();
        }
        public void stop_monitor()
        {
            writter.control_monitor('s');
        }
        public void pause_monitor()
        {
            writter.control_monitor('p');
        }
        public void close_monitor()
        {
            writter.control_monitor('c');
        }
        public void recovery_monitor()
        {
            writter.control_monitor('r');
        }

        /// <summary>
        /// 使用一个私有的writter
        /// </summary>
        public void use_private_writter()
        {
            pri = true;
            writter = Writter.get_new_writter();
        }


        public void error(string message)
        {
            if (Log_level>= Level.error)
            {
                writter.write(new Log(message, type, Level.error));
            }
            
        }
        public void warning(string message)
        {
            if (Log_level >= Level.warning)
            {
                writter.write(new Log(message, type, Level.warning));
            }
        }

        public void info(string message)
        {
            if (Log_level >= Level.info)
            {
                writter.write(new Log(message, type, Level.info));
            }
        }

        public void debug(string message)
        {
            if (Log_level >= Level.debug)
            {
                writter.write(new Log(message, type, Level.debug));
            }
        }


    }

    //写操作的公有实现
    public class Writter
    {
        private static Writter unique_instance;

        public enum Work_type { file,console,ram}


        private string file_path;
        FileStream fs_writer;

        private string console_ram_name;
        private MemoryMappedFile console_message_ram;//保存消息
        private MemoryMappedFile console_position_ram;//保存标志位
        private MemoryMappedViewStream console_stream;//读写消息的流
        private MemoryMappedViewAccessor console_access;//读写标志
        private char have_data;//标志有无数据，t，f
        private char rwf_falg;//标志内存状态，r（读取中），w（写入中），f（空闲）
        private char control;//控制log显示器进程运行状态，r（运行），p（暂停），s（停止但不退出），c（关闭程序）
        BinaryWriter b_writer ;//流的读写器
        Process monitor;
        char monitor_state = 'n';//表示未初始化,其他同control
        Mutex mutex;//锁

        private string ram_file_path;
        private MemoryMappedFile memory_file;
        private MemoryMappedViewStream ram_file_stream;
        private long memory_capacity = 1024*1024;//kb

        //日志缓存池
        LinkedList<Log> logs = new LinkedList<Log>();

        /// <summary>
        /// 设置内存文件名
        /// </summary>
        public string Ram_file_name { get => ram_file_path; set => ram_file_path = value; }

        public string File_path { get => file_path; set => file_path = value; }
        public MemoryMappedFile Memory_file { get => memory_file; set => memory_file = value; }
        /// <summary>
        /// 设置内存大小kb
        /// </summary>
        public int Memory_capacity { get => (int)(memory_capacity/1024); set => memory_capacity = value*1024; }



        public static Writter get_writter()
        {
            if (unique_instance==null)
            {
                unique_instance = new Writter();
            }
            return unique_instance;
        }

        /// <summary>
        /// 此操作创建临时writter
        /// </summary>
        /// <returns></returns>
        public static Writter get_new_writter()
        {
            return new Writter();
        }



        private Writter()
        {
            
            Thread thread = new Thread(new ThreadStart(listiner));
            thread.Start();
        }
        /// <summary>
        /// 初始化内存文件
        /// </summary>
        public void init_memory_file()
        {
            if (ram_file_path==null||ram_file_path== string.Empty)
            {
                throw new IOException("error: illegal ram_file_path");
            }
            if (memory_capacity<=0)
            {
                throw new IOException("error: memory_capacity<=0");
            }
            if (Memory_file == null)
            {
                Memory_file = MemoryMappedFile.CreateOrOpen(ram_file_path, Memory_capacity);  // 创建指定大小的内存文件，会在应用程序退出时自动释放
            }
            if (ram_file_stream == null)
            {
                ram_file_stream = Memory_file.CreateViewStream();           // 访问内存文件对象
            }
        }

        /// <summary>
        /// 初始化磁盘文件
        /// </summary>
        public void init_file()
        {
            if (file_path==null||file_path==string.Empty)
            {
                throw new IOException("error: illegal file_path");
            }
            fs_writer = new FileStream(file_path, FileMode.Append, FileAccess.Write);
            
        }

        /// <summary>
        /// 初始化共享内存和写入流
        /// </summary>
        public void init_console()
        {
            //初始化消息空间
            if (console_ram_name == null || console_ram_name == string.Empty)
            {
                //获得唯一映射名
                console_ram_name = DateTime.Now.ToString("yyyyMMddHHmmssms");
                System.Security.Cryptography.MD5CryptoServiceProvider md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
                console_ram_name = BitConverter.ToString(md5.ComputeHash(System.Text.UTF8Encoding.Default.GetBytes(console_ram_name)), 4, 8);
                console_ram_name = console_ram_name.Replace("-", "");
                //console_ram_name = Regex.Replace(console_ram_name, @"\d", "");//正则替换去掉数字
                char[] temp = new char[16];
                for (int i = 0; i < 16; i++)
                {
                    temp[i] = (char)((int)console_ram_name[i] + 17);
                }
                console_ram_name = new string(temp);
                temp = null;

            }
            if (console_message_ram==null)
            {
                //创建共享内存，大小1K
                console_message_ram = MemoryMappedFile.CreateNew(console_ram_name, 1024);
            }
            if (console_stream==null)
            {
                //创建写入流
                console_stream = console_message_ram.CreateViewStream();
            }
            if (b_writer==null)
            {
                b_writer = new BinaryWriter(console_stream);
            }
            //初始化标志空间
            if (console_position_ram == null)
            {
                //创建标志位内存，大小16字节
                console_position_ram = MemoryMappedFile.CreateNew(console_ram_name.Substring(0,8), 16);
            }
            if (console_access==null)
            {
                console_access = console_position_ram.CreateViewAccessor();
                console_access.Write(0, 'f');
                console_access.Write(2, 'f');
                console_access.Write(4, 'r');
            }
            if (mutex==null)
            {
                //创建锁
                bool mutexCreated;
                mutex = new Mutex(true, console_ram_name.Substring(8, 8), out mutexCreated);
                if (mutexCreated==false)
                {
                    throw new Exception("创建锁失败");
                }
                //释放锁
                mutex.ReleaseMutex();
            }

            //拉起monitor进程
            if (monitor_state=='n')
            {
                start_monitor();
            }
        }

        private void start_monitor()
        {
            if (File.Exists(@"./extraEXE/message_monitor.exe"))
            {
                monitor = new Process();
                monitor.StartInfo.FileName = System.IO.Directory.GetCurrentDirectory() + "/extraEXE/message_monitor.exe";
                monitor.StartInfo.Arguments = console_ram_name;
                monitor.StartInfo.CreateNoWindow = false;
                monitor.StartInfo.UseShellExecute = true;
                monitor.Start();
                control = 'r';
                monitor_state = 'r';
            }
            else
            {
                throw new Exception("can not find file");
            }
            
        }

        public void control_monitor(char c)
        {
            if(c=='c')
            {
                lock (this)
                {
                    monitor_state = 'c';
                }
            }
            control = c;
            mutex.WaitOne();//获得锁
            console_access.Write(4, c);
            mutex.ReleaseMutex();
        }
        public void allow_start_monitor()
        {
            lock (this)
            {
                if (monitor_state=='c')
                {
                    monitor_state = 'n';
                }
            }

            Console.WriteLine("allow_start_monitor()");
        }


        /// <summary>
        /// 清理内存文件
        /// </summary>
        public void Dispose_memory()
        {
            if (ram_file_stream != null)
            {
                ram_file_stream.Flush();
            }
            if (Memory_file!=null)
            {
                Memory_file.Dispose();
            }
        }


        //写操作在新线程执行
        private void listiner()
        {
            Log now_log;
            byte[] temp;
            int no_data_time=0;

            while (true)
            {
                //Console.WriteLine("logs.Count="+logs.Count);
                if (logs.Count==0)//无日志时等待
                {
                    no_data_time++;
                    if (no_data_time > 5)
                    {
                        no_data_time = 0;
                        Thread.Sleep(1);
                    }
                    else
                    {
                        Thread.Sleep(0);
                    }
                    continue;
                }
                lock (this)
                {
                    now_log = logs.First();
                    logs.RemoveFirst();
                }
                
                switch (now_log.type)
                {
                    case Work_type.file:
                        if (fs_writer==null)
                        {
                            init_file();
                        }
                        temp = now_log.get_bytes();
                        fs_writer.Write(temp, 0, temp.Length);
                        break;
                    case Work_type.console:
                        lock (this)
                        {
                            if (monitor_state == 'c')
                            {
                                logs.AddLast(now_log);//加入队列尾避免丢失
                                continue;
                            }
                        }
                        if (console_stream==null||monitor_state=='n')
                        {
                            init_console();
                        }
                        int fail_num = 0;
                        while (true)
                        {
                            if (control!='r')
                            {
                                if (fail_num>20)
                                {
                                    Thread.Sleep(1);
                                    lock (this)
                                    {
                                        logs.AddLast(now_log);//加入队列尾避免丢失
                                    }
                                    break;
                                }
                                fail_num++;
                                Thread.Sleep(0);
                                continue;
                            }

                            mutex.WaitOne();//获得锁


                            //读状态
                            have_data = console_access.ReadChar(0);
                            rwf_falg = console_access.ReadChar(2);
                            control= console_access.ReadChar(4);

                            //Console.WriteLine("have_data=" + have_data + " rwf_flag=" + rwf_falg + " control=" + control);

                            if (rwf_falg != 'f'&& rwf_falg != 'r'&& rwf_falg != 'w')
                            {
                                //rwf_falg = 'f';
                                throw new Exception("error data");
                            }
                            if (rwf_falg == 'f'&&have_data=='f')
                            {
                                console_access.Write(2, 'w');

                                b_writer.BaseStream.Seek(0, SeekOrigin.Begin);
                                b_writer.BaseStream.Flush();

                                
                                
                                b_writer.Write(now_log.ToString());
                                console_access.Write(0, 't');
                                //console_access.Write(4, 'r');

                                //Console.WriteLine("have_data=" + have_data + " rwf_flag=" + rwf_falg + " control=" + control);

                                //写入完成
                                console_access.Write(2, 'f');
                                
                                mutex.ReleaseMutex();

                                break;
                            }
                            else //当前繁忙
                            {
                                mutex.ReleaseMutex();
                                //Console.WriteLine("tooooo busy");
                                Thread.Sleep(0);//放弃时间片
                                //Thread.Sleep(1);//挂起1毫秒
                            }
                        }
                        
                        break;
                    case Work_type.ram:
                        if (ram_file_stream==null)
                        {
                            init_memory_file();
                        }
                        temp = now_log.get_bytes();
                        ram_file_stream.Write(temp, 0, temp.Length);
                        break;
                    default:
                        break;
                }
            }
        }


        //写操作对外接口
        public void write(Log input)
        {
            lock (this)
            {
                logs.AddLast(input);
            }
            
        }



    }

    //每条日志结构
    public class Log
    {
        DateTime message_time;
        string message;
        public Writter.Work_type type;
        Logger.Level level;

        public Log(string message, Writter.Work_type type, Logger.Level level)
        {
            message_time = DateTime.Now;
            this.type = type;
            this.message = message;
            this.level = level;
        }

        public override string ToString()
        {
            return message_time.ToString() + "-[" + (int)level + "]-:" + message;
        }

        public byte[] get_bytes()
        {
            return Encoding.UTF8.GetBytes(this.ToString() + Environment.NewLine);
        }
        
    }
}
