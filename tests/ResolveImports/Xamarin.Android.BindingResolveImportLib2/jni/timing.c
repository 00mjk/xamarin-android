#include <android/log.h>
#include <sys/time.h>
#include <jni.h>

void
foo_void_timing (void)
{
}

int
foo_int_timing (void)
{
	return 0;
}

void*
foo_ptr_timing (void)
{
	return 0;
}

void
foo_void_a1_timing (void *obj1)
{
}

void
foo_void_a2_timing (void *obj1, void *obj2)
{
}

void
foo_void_a3_timing (void *obj1, void *obj2, void *obj3)
{
}

void
foo_void_ai1_timing (int i1)
{
}

void
foo_void_ai2_timing (int i1, int i2)
{
}

void
foo_void_ai3_timing (int i1, int i2, int i3)
{
}

struct FooMethods {
	void  (*instance_void)(void);
	int   (*instance_int)(void);
	void* (*instance_ptr)(void);

	void  (*void_1_args)(void *);
	void  (*void_2_args)(void *, void *);
	void  (*void_3_args)(void *, void *, void *);

	void  (*void_1_iargs)(int);
	void  (*void_2_iargs)(int, int);
	void  (*void_3_iargs)(int, int, int);
};

void
foo_get_methods (struct FooMethods* methods)
{
	methods->instance_void = foo_void_timing;
	methods->instance_int  = foo_int_timing;
	methods->instance_ptr  = foo_ptr_timing;

	methods->void_1_args   = foo_void_a1_timing;
	methods->void_2_args   = foo_void_a2_timing;
	methods->void_3_args   = foo_void_a3_timing;

	methods->void_1_iargs   = foo_void_ai1_timing;
	methods->void_2_iargs   = foo_void_ai2_timing;
	methods->void_3_iargs   = foo_void_ai3_timing;
}

static jmethodID Timing_StaticVoidMethod;
static jmethodID Timing_StaticIntMethod;
static jmethodID Timing_StaticObjectMethod;

static jmethodID Timing_VirtualVoidMethod;
static jmethodID Timing_VirtualIntMethod;
static jmethodID Timing_VirtualObjectMethod;

static jmethodID Timing_FinalVoidMethod;
static jmethodID Timing_FinalIntMethod;
static jmethodID Timing_FinalObjectMethod;

static jmethodID Timing_StaticVoidMethod1Args;
static jmethodID Timing_StaticVoidMethod2Args;
static jmethodID Timing_StaticVoidMethod3Args;

static jmethodID Timing_StaticVoidMethod1IArgs;
static jmethodID Timing_StaticVoidMethod2IArgs;
static jmethodID Timing_StaticVoidMethod3IArgs;

static jclass Object_class;
static jmethodID Object_init;

static void    (*CallStaticVoidMethod)(JNIEnv*, jclass, jmethodID, ...);
static int     (*CallStaticIntMethod)(JNIEnv*, jclass, jmethodID, ...);
static jobject (*CallStaticObjectMethod)(JNIEnv*, jclass, jmethodID, ...);

static void    (*CallVoidMethod)(JNIEnv*, jobject, jmethodID, ...);
static int     (*CallIntMethod)(JNIEnv*, jobject, jmethodID, ...);
static jobject (*CallObjectMethod)(JNIEnv*, jobject, jmethodID, ...);

static void    (*CallNonvirtualVoidMethod)(JNIEnv*, jobject, jclass, jmethodID, ...);
static int     (*CallNonvirtualIntMethod)(JNIEnv*, jobject, jclass, jmethodID, ...);
static jobject (*CallNonvirtualObjectMethod)(JNIEnv*, jobject, jclass, jmethodID, ...);

void
foo_init (JNIEnv *env)
{
	jclass Timing, Object;

	__android_log_print (ANDROID_LOG_INFO, "*jonp*", "foo_init; env=%p", env);

	/* libbar.so loading test */
	if (!env)
		return;

	Timing = (*env)->FindClass (env, "com/xamarin/android/Timing");
	if (!Timing)
		return;

	Object = (*env)->FindClass (env, "java/lang/Object");
	if (!Object)
		return;
	Object_class = (*env)->NewGlobalRef (env, Object);
	(*env)->DeleteLocalRef (env, Object);

	Object_init = (*env)->GetMethodID (env, Object_class, "<init>", "()V");

	CallStaticVoidMethod    = (*env)->CallStaticVoidMethod;
	CallStaticIntMethod     = (*env)->CallStaticIntMethod;
	CallStaticObjectMethod  = (*env)->CallStaticObjectMethod;

	CallVoidMethod    = (*env)->CallVoidMethod;
	CallIntMethod     = (*env)->CallIntMethod;
	CallObjectMethod  = (*env)->CallObjectMethod;

	CallNonvirtualVoidMethod    = (*env)->CallNonvirtualVoidMethod;
	CallNonvirtualIntMethod     = (*env)->CallNonvirtualIntMethod;
	CallNonvirtualObjectMethod  = (*env)->CallNonvirtualObjectMethod;

	Timing_StaticVoidMethod = (*env)->GetStaticMethodID (env, Timing,
			"StaticVoidMethod", "()V");
	Timing_StaticIntMethod = (*env)->GetStaticMethodID (env, Timing,
			"StaticIntMethod", "()I");
	Timing_StaticObjectMethod = (*env)->GetStaticMethodID (env, Timing,
			"StaticObjectMethod", "()Ljava/lang/Object;");

	Timing_VirtualVoidMethod = (*env)->GetMethodID (env, Timing,
			"VirtualVoidMethod", "()V");
	Timing_VirtualIntMethod = (*env)->GetMethodID (env, Timing,
			"VirtualIntMethod", "()I");
	Timing_VirtualObjectMethod = (*env)->GetMethodID (env, Timing,
			"VirtualObjectMethod", "()Ljava/lang/Object;");

	Timing_FinalVoidMethod = (*env)->GetMethodID (env, Timing,
			"FinalVoidMethod", "()V");
	Timing_FinalIntMethod = (*env)->GetMethodID (env, Timing,
			"FinalIntMethod", "()I");
	Timing_FinalObjectMethod = (*env)->GetMethodID (env, Timing,
			"FinalObjectMethod", "()Ljava/lang/Object;");

	Timing_StaticVoidMethod1Args = (*env)->GetStaticMethodID (env, Timing,
			"StaticVoidMethod1Args", "(Ljava/lang/Object;)V");
	Timing_StaticVoidMethod2Args = (*env)->GetStaticMethodID (env, Timing,
			"StaticVoidMethod2Args", "(Ljava/lang/Object;Ljava/lang/Object;)V");
	Timing_StaticVoidMethod3Args = (*env)->GetStaticMethodID (env, Timing,
			"StaticVoidMethod3Args", "(Ljava/lang/Object;Ljava/lang/Object;Ljava/lang/Object;)V");

	Timing_StaticVoidMethod1IArgs = (*env)->GetStaticMethodID (env, Timing,
			"StaticVoidMethod1IArgs", "(I)V");
	Timing_StaticVoidMethod2IArgs = (*env)->GetStaticMethodID (env, Timing,
			"StaticVoidMethod2IArgs", "(II)V");
	Timing_StaticVoidMethod3IArgs = (*env)->GetStaticMethodID (env, Timing,
			"StaticVoidMethod3IArgs", "(III)V");

	(*env)->DeleteLocalRef (env, Timing);
}

static long long
current_time_millis (void)
{
	struct timeval tv;

	gettimeofday(&tv, (struct timezone *) NULL);
	long long when = tv.tv_sec * 1000LL + tv.tv_usec / 1000;
	return when;
}

void
foo_get_native_jni_timings (JNIEnv *env, int count, jclass klass, jobject self, long long *jniTimes)
{
	int i;
	long long start, end;

	jobject obj1 = (*env)->NewObject(env, Object_class, Object_init),
		obj2 = (*env)->NewObject(env, Object_class, Object_init),
		obj3 = (*env)->NewObject(env, Object_class, Object_init);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallStaticVoidMethod (env, klass, Timing_StaticVoidMethod);
	end = current_time_millis ();

	jniTimes [0] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*jonp*", "foo/timing: static void    method invoke: %lli ms", jniTimes [0]);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallStaticIntMethod (env, klass, Timing_StaticIntMethod);
	end = current_time_millis ();

	jniTimes [1] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*jonp*", "foo/timing: static int     method invoke: %lli ms", jniTimes [1]);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallStaticObjectMethod (env, klass, Timing_StaticObjectMethod);
	end = current_time_millis ();

	jniTimes [2] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*jonp*", "foo/timing: static Object  method invoke: %lli ms", jniTimes [2]);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallVoidMethod (env, self, Timing_VirtualVoidMethod);
	end = current_time_millis ();

	jniTimes [3] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*jonp*", "foo/timing: virtual void   method invoke: %lli ms", jniTimes [3]);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallIntMethod (env, self, Timing_VirtualIntMethod);
	end = current_time_millis ();

	jniTimes [4] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*jonp*", "foo/timing: virtual int    method invoke: %lli ms", jniTimes [4]);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallObjectMethod (env, self, Timing_VirtualObjectMethod);
	end = current_time_millis ();

	jniTimes [5] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*jonp*", "foo/timing: virtual Object method invoke: %lli ms", jniTimes [5]);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallNonvirtualVoidMethod (env, self, klass, Timing_FinalVoidMethod);
	end = current_time_millis ();

	jniTimes [6] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*jonp*", "foo/timing: final void     method invoke: %lli ms", jniTimes [6]);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallNonvirtualIntMethod (env, self, klass, Timing_FinalIntMethod);
	end = current_time_millis ();

	jniTimes [7] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*jonp*", "foo/timing: final int      method invoke: %lli ms", jniTimes [7]);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallNonvirtualObjectMethod (env, self, klass, Timing_FinalObjectMethod);
	end = current_time_millis ();

	jniTimes [8] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*jonp*", "foo/timing: final Object   method invoke: %lli ms", jniTimes [8]);



	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallStaticVoidMethod (env, klass, Timing_StaticVoidMethod1Args, obj1);
	end = current_time_millis ();

	jniTimes [9] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*alexrp*", "foo/timing: static void o1 method invoke: %lli ms", jniTimes [9]);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallStaticVoidMethod (env, klass, Timing_StaticVoidMethod2Args, obj1, obj2);
	end = current_time_millis ();

	jniTimes [10] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*alexrp*", "foo/timing: static void o2 method invoke: %lli ms", jniTimes [10]);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallStaticVoidMethod (env, klass, Timing_StaticVoidMethod3Args, obj1, obj2, obj3);
	end = current_time_millis ();

	jniTimes [11] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*alexrp*", "foo/timing: static void o3 method invoke: %lli ms", jniTimes [11]);



	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallStaticVoidMethod (env, klass, Timing_StaticVoidMethod1IArgs, 42);
	end = current_time_millis ();

	jniTimes [12] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*alexrp*", "foo/timing: static void i1 method invoke: %lli ms", jniTimes [12]);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallStaticVoidMethod (env, klass, Timing_StaticVoidMethod2IArgs, 42, 42);
	end = current_time_millis ();

	jniTimes [13] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*alexrp*", "foo/timing: static void i2 method invoke: %lli ms", jniTimes [13]);

	start = current_time_millis ();
	for (i = 0; i < count; i++)
		CallStaticVoidMethod (env, klass, Timing_StaticVoidMethod3IArgs, 42, 42, 42);
	end = current_time_millis ();

	jniTimes [14] = end - start;
	__android_log_print (ANDROID_LOG_INFO, "*alexrp*", "foo/timing: static void i3 method invoke: %lli ms", jniTimes [14]);
}

