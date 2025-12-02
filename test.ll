; ModuleID = 'languageOcompiler'
source_filename = "languageO"
%Array = type { i32, i8* }

declare i8* @malloc(i64)
declare %Array* @o_array_new(i32)
declare i32 @o_array_length(%Array*)
declare i8* @o_array_get(%Array*, i32)
declare void @o_array_set(%Array*, i32, i8*)
declare i32 @printf(i8*, ...)

@.fmt_int = private unnamed_addr constant [4 x i8] c"%d\0A\00"
@.fmt_real = private unnamed_addr constant [4 x i8] c"%f\0A\00"

%Sample = type { i32 }
%Main = type { i32, %Sample* }

define void @Sample_ctor(%Sample* %this) {
entry:
  ret void
}

define void @Main_ctor(%Main* %this) {
entry:
  %t0 = getelementptr %Sample, %Sample* null, i32 1
  %t1 = ptrtoint %Sample* %t0 to i64
  %t2 = call i8* @malloc(i64 %t1)
  %t3 = bitcast i8* %t2 to %Sample*
  %t4 = getelementptr %Sample, %Sample* %t3, i32 0, i32 0
  store i32 1, i32* %t4
  call void @Sample_ctor(%Sample* %t3)
  %t5 = getelementptr %Main, %Main* %this, i32 0, i32 1
  store %Sample* %t3, %Sample** %t5
  %t6 = getelementptr %Main, %Main* %this, i32 0, i32 1
  %t7 = load %Sample*, %Sample** %t6
  %t8 = getelementptr %Sample, %Sample* %t7, i32 0, i32 0
  %t9 = load i32, i32* %t8
  %t10 = alloca i32
  switch i32 %t9, label %dispatch_default_0 [
      i32 1, label %dispatch_Sample_2
  ]
dispatch_Sample_2:
  %t11 = call i32 @Sample_foo(%Sample* %t7)
  store i32 %t11, i32* %t10
  br label %dispatch_merge_1
dispatch_default_0:
  store i32 0, i32* %t10
  br label %dispatch_merge_1
dispatch_merge_1:
  %t12 = load i32, i32* %t10
  %t13 = getelementptr [4 x i8], [4 x i8]* @.fmt_int, i32 0, i32 0
  call i32 (i8*, ...) @printf(i8* %t13, i32 %t12)
  %t14 = alloca i32
  store i32 %t12, i32* %t14
  ret void
}

define i32 @Sample_foo(%Sample* %this) {
entry:
  ret i32 1
}

define i32 @main()
{
entry:
  %size_ptr = getelementptr %Main, %Main* null, i32 1
  %size = ptrtoint %Main* %size_ptr to i64
  %raw = call i8* @malloc(i64 %size)
  %obj = bitcast i8* %raw to %Main*
  %class_id_ptr = getelementptr %Main, %Main* %obj, i32 0, i32 0
  store i32 2, i32* %class_id_ptr
  call void @Main_ctor(%Main* %obj)
  ret i32 0
}

