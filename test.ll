; ModuleID = 'languageOcompiler'
source_filename = "languageO"
%Array = type { i32, i8* }
%List = type { i8* }

declare i8* @malloc(i64)
declare %Array* @o_array_new(i32)
declare i32 @o_array_length(%Array*)
declare i8* @o_array_get(%Array*, i32)
declare void @o_array_set(%Array*, i32, i8*)
declare %List* @o_list_empty()
declare %List* @o_list_singleton(i8*)
declare %List* @o_list_replicate(i8*, i32)
declare %List* @o_list_append(%List*, i8*)
declare i8* @o_list_head(%List*)
declare %List* @o_list_tail(%List*)
declare %Array* @o_list_to_array(%List*)
declare i32 @printf(i8*, ...)

@.fmt_int = private unnamed_addr constant [4 x i8] c"%d\0A\00"
@.fmt_real = private unnamed_addr constant [4 x i8] c"%f\0A\00"

%Sample = type { i32, i32 }
%Main = type { i32 }

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
  %t5 = alloca %Sample*
  store %Sample* %t3, %Sample** %t5
  %t6 = load %Sample*, %Sample** %t5
  %t7 = getelementptr %Sample, %Sample* %t6, i32 0, i32 0
  %t8 = load i32, i32* %t7
  %t9 = alloca i32
  switch i32 %t8, label %dispatch_default_0 [
      i32 1, label %dispatch_Sample_2
  ]
dispatch_Sample_2:
  %t10 = call i32 @Sample_M1(%Sample* %t6)
  store i32 %t10, i32* %t9
  br label %dispatch_merge_1
dispatch_default_0:
  store i32 0, i32* %t9
  br label %dispatch_merge_1
dispatch_merge_1:
  %t11 = load i32, i32* %t9
  %t12 = getelementptr [4 x i8], [4 x i8]* @.fmt_int, i32 0, i32 0
  call i32 (i8*, ...) @printf(i8* %t12, i32 %t11)
  %t13 = alloca i32
  store i32 %t11, i32* %t13
  ret void
}

define i32 @Sample_M1(%Sample* %this) {
entry:
  %t0 = getelementptr %Sample, %Sample* %this, i32 0, i32 1
  %t1 = load i32, i32* %t0
  %t2 = add i32 %t1, 2
  ret i32 %t2
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

